using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Demo
{
    internal sealed class GlobalKeyboardHook : IDisposable
    {
        private readonly HookProc hookProc;
        private IntPtr hookHandle;

        public GlobalKeyboardHook()
        {
            hookProc = HookCallback;
            hookHandle = SetHook(hookProc);
        }

        public event EventHandler<GlobalKeyboardHookEventArgs> KeyboardPressed;
        public event Action<object, Exception> HookError;

        public void Dispose()
        {
            if (hookHandle == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }

        private static IntPtr SetHook(HookProc proc)
        {
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
            {
                if (module == null)
                {
                    throw new Win32Exception("Unable to read the current process module.");
                }

                var moduleHandle = GetModuleHandle(module.ModuleName);
                var handle = SetWindowsHookEx(WhKeyboardLl, proc, moduleHandle, 0);
                if (handle == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return handle;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
            {
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);
            }

            var message = unchecked((int)wParam.ToInt64());
            if (message != WmKeydown &&
                message != WmKeyup &&
                message != WmSyskeydown &&
                message != WmSyskeyup)
            {
                return CallNextHookEx(hookHandle, nCode, wParam, lParam);
            }

            try
            {
                var hookStruct = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
                var args = new GlobalKeyboardHookEventArgs(
                    (Keys)hookStruct.vkCode,
                    message == WmKeydown || message == WmSyskeydown,
                    (hookStruct.flags & LlkhfInjected) == LlkhfInjected,
                    hookStruct.scanCode);

                var handler = KeyboardPressed;
                if (handler != null)
                {
                    handler(this, args);
                }

                return args.Handled ? new IntPtr(1) : CallNextHookEx(hookHandle, nCode, wParam, lParam);
            }
            catch (Exception ex)
            {
                var errorHandler = HookError;
                if (errorHandler != null)
                {
                    errorHandler(this, ex);
                }

                return CallNextHookEx(hookHandle, nCode, wParam, lParam);
            }
        }

        private const int WhKeyboardLl = 13;
        private const int WmKeydown = 0x0100;
        private const int WmKeyup = 0x0101;
        private const int WmSyskeydown = 0x0104;
        private const int WmSyskeyup = 0x0105;
        private const int LlkhfInjected = 0x10;

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public int flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
