// ==============================================
// 文件名: GlobalKeyboardHook.cs
// 功能描述: 全局键盘钩子类
// 用于捕获系统级的键盘事件，实现全局按键监听
// ==============================================

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Demo
{
    /// <summary>
    /// 全局键盘钩子类
    /// 实现对系统键盘事件的全局监听
    /// </summary>
    internal sealed class GlobalKeyboardHook : IDisposable
    {
        /// <summary>
        /// 钩子回调委托
        /// </summary>
        private readonly HookProc hookProc;

        /// <summary>
        /// 钩子句柄
        /// </summary>
        private IntPtr hookHandle;
        private readonly Thread hookThread;
        private readonly ManualResetEventSlim hookReady = new ManualResetEventSlim(false);
        private Exception hookInitializationException;
        private int hookThreadId;
        private bool disposed;

        /// <summary>
        /// 构造函数
        /// 初始化并安装全局键盘钩子
        /// </summary>
        public GlobalKeyboardHook()
        {
            hookProc = HookCallback;
            hookThread = new Thread(HookThreadMain);
            hookThread.IsBackground = true;
            hookThread.Name = "GlobalKeyboardHookThread";
            hookThread.Start();
            hookReady.Wait();
            if (hookInitializationException != null)
            {
                throw new InvalidOperationException("无法初始化全局键盘钩子。", hookInitializationException);
            }
        }

        /// <summary>
        /// 键盘按键事件
        /// 当检测到键盘按键时触发
        /// </summary>
        public event EventHandler<GlobalKeyboardHookEventArgs> KeyboardPressed;

        /// <summary>
        /// 钩子错误事件
        /// 当钩子处理过程中发生异常时触发
        /// </summary>
        public event Action<object, Exception> HookError;

        /// <summary>
        /// 释放资源
        /// 卸载键盘钩子
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (hookThreadId != 0)
            {
                PostThreadMessage(hookThreadId, WmAppQuit, UIntPtr.Zero, IntPtr.Zero);
                hookThread.Join(2000);
            }

            UnhookCurrentHandle();
            hookReady.Dispose();
        }

        /// <summary>
        /// 重置钩子
        /// 卸载并重新安装键盘钩子，用于恢复钩子状态
        /// </summary>
        public void Reset()
        {
            if (disposed || hookThreadId == 0)
            {
                return;
            }

            PostThreadMessage(hookThreadId, WmAppResetHook, UIntPtr.Zero, IntPtr.Zero);
        }

        /// <summary>
        /// 设置键盘钩子
        /// </summary>
        /// <param name="proc">钩子回调函数</param>
        /// <returns>钩子句柄</returns>
        private static IntPtr SetHook(HookProc proc)
        {
            using (var process = Process.GetCurrentProcess())
            using (var module = process.MainModule)
            {
                if (module == null)
                {
                    throw new Win32Exception("无法读取当前进程模块。");
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

        /// <summary>
        /// 钩子回调函数
        /// 处理键盘事件并触发相应的事件通知
        /// </summary>
        /// <param name="nCode">钩子代码</param>
        /// <param name="wParam">消息参数</param>
        /// <param name="lParam">消息参数</param>
        /// <returns>处理结果</returns>
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            var currentHookHandle = hookHandle;

            // 如果 nCode < 0，必须传递给下一个钩子
            if (nCode < 0)
            {
                return CallNextHookEx(currentHookHandle, nCode, wParam, lParam);
            }

            var message = unchecked((int)wParam.ToInt64());
            // 只处理键盘相关的消息
            if (message != WmKeydown &&
                message != WmKeyup &&
                message != WmSyskeydown &&
                message != WmSyskeyup)
            {
                return CallNextHookEx(currentHookHandle, nCode, wParam, lParam);
            }

            GlobalKeyboardHookEventArgs args = null;
            try
            {
                var hookStruct = (KbdLlHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLlHookStruct));
                args = new GlobalKeyboardHookEventArgs(
                    (Keys)hookStruct.vkCode,
                    message == WmKeydown || message == WmSyskeydown,
                    (hookStruct.flags & LlkhfInjected) == LlkhfInjected,
                    hookStruct.scanCode);

                var handler = KeyboardPressed;
                if (handler != null)
                {
                    handler(this, args);
                }

                return args.Handled ? new IntPtr(1) : CallNextHookEx(currentHookHandle, nCode, wParam, lParam);
            }
            catch (Exception ex)
            {
                var errorHandler = HookError;
                if (errorHandler != null)
                {
                    errorHandler(this, ex);
                }

                if (args != null && args.Handled)
                {
                    return new IntPtr(1);
                }

                return CallNextHookEx(currentHookHandle, nCode, wParam, lParam);
            }
        }

        /// <summary>
        /// 卸载当前钩子句柄
        /// </summary>
        private void UnhookCurrentHandle()
        {
            if (hookHandle == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(hookHandle);
            hookHandle = IntPtr.Zero;
        }

        private void HookThreadMain()
        {
            try
            {
                hookThreadId = GetCurrentThreadId();
                hookHandle = SetHook(hookProc);
            }
            catch (Exception ex)
            {
                hookInitializationException = ex;
                hookReady.Set();
                return;
            }

            hookReady.Set();

            NativeMessage message;
            while (GetMessage(out message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.message == WmAppResetHook)
                {
                    try
                    {
                        UnhookCurrentHandle();
                        hookHandle = SetHook(hookProc);
                    }
                    catch (Exception ex)
                    {
                        var errorHandler = HookError;
                        if (errorHandler != null)
                        {
                            errorHandler(this, ex);
                        }
                    }

                    continue;
                }

                if (message.message == WmAppQuit)
                {
                    break;
                }
            }

            UnhookCurrentHandle();
        }

        // 钩子类型常量
        private const int WhKeyboardLl = 13;
        private const uint WmAppResetHook = 0x8000 + 1;
        private const uint WmAppQuit = 0x8000 + 2;

        // Windows 消息常量
        private const int WmKeydown = 0x0100;
        private const int WmKeyup = 0x0101;
        private const int WmSyskeydown = 0x0104;
        private const int WmSyskeyup = 0x0105;

        // 钩子标志常量
        private const int LlkhfInjected = 0x10;

        /// <summary>
        /// 钩子回调委托类型
        /// </summary>
        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// 键盘钩子结构
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;      // 虚拟键码
            public uint scanCode;    // 扫描码
            public int flags;        // 标志
            public uint time;        // 时间戳
            public IntPtr dwExtraInfo; // 额外信息
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public UIntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public System.Drawing.Point pt;
        }

        // Win32 API 声明
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(int idThread, uint msg, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern sbyte GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("kernel32.dll")]
        private static extern int GetCurrentThreadId();
    }
}
