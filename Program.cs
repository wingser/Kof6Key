// ==============================================
// 文件名: Program.cs
// 功能描述: 程序入口类
// 负责启动应用程序
// ==============================================

using System;
using System.Windows.Forms;

namespace Demo
{
    /// <summary>
    /// 程序入口类
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点
        /// </summary>
        [STAThread]
        private static void Main()
        {
            // 启用视觉样式
            Application.EnableVisualStyles();
            // 设置默认文本呈现为兼容模式
            Application.SetCompatibleTextRenderingDefault(false);
            // 运行主窗体
            Application.Run(new Main());
        }
    }
}