// ==============================================
// 文件名: GlobalKeyboardHookEventArgs.cs
// 功能描述: 全局键盘钩子事件参数类
// 用于在键盘钩子触发时传递按键信息
// ==============================================

using System;
using System.Windows.Forms;

namespace Demo
{
    /// <summary>
    /// 全局键盘钩子事件参数类
    /// 封装键盘事件的相关信息
    /// </summary>
    internal sealed class GlobalKeyboardHookEventArgs : EventArgs
    {
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="keyCode">虚拟键码</param>
        /// <param name="isKeyDown">是否为按键按下</param>
        /// <param name="isInjected">是否为注入的按键事件</param>
        /// <param name="scanCode">扫描码</param>
        public GlobalKeyboardHookEventArgs(Keys keyCode, bool isKeyDown, bool isInjected, uint scanCode)
        {
            KeyCode = keyCode;
            IsKeyDown = isKeyDown;
            IsInjected = isInjected;
            ScanCode = scanCode;
        }

        /// <summary>
        /// 获取虚拟键码
        /// </summary>
        public Keys KeyCode { get; private set; }

        /// <summary>
        /// 获取是否为按键按下状态
        /// </summary>
        public bool IsKeyDown { get; private set; }

        /// <summary>
        /// 获取是否为按键释放状态
        /// </summary>
        public bool IsKeyUp
        {
            get { return !IsKeyDown; }
        }

        /// <summary>
        /// 获取是否为程序注入的按键事件
        /// 用于区分用户实际按键和程序模拟的按键
        /// </summary>
        public bool IsInjected { get; private set; }

        /// <summary>
        /// 获取按键的扫描码
        /// </summary>
        public uint ScanCode { get; private set; }

        /// <summary>
        /// 设置或获取事件是否已处理
        /// 设置为 true 可阻止事件传递给其他应用程序
        /// </summary>
        public bool Handled { get; set; }
    }
}