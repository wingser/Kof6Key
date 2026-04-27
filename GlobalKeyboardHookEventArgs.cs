using System;
using System.Windows.Forms;

namespace Demo
{
    internal sealed class GlobalKeyboardHookEventArgs : EventArgs
    {
        public GlobalKeyboardHookEventArgs(Keys keyCode, bool isKeyDown, bool isInjected, uint scanCode)
        {
            KeyCode = keyCode;
            IsKeyDown = isKeyDown;
            IsInjected = isInjected;
            ScanCode = scanCode;
        }

        public Keys KeyCode { get; private set; }

        public bool IsKeyDown { get; private set; }

        public bool IsKeyUp
        {
            get { return !IsKeyDown; }
        }

        public bool IsInjected { get; private set; }

        public uint ScanCode { get; private set; }

        public bool Handled { get; set; }
    }
}
