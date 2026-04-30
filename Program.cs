// ==============================================
// 文件名: Program.cs
// 功能描述: 程序入口类
// 负责启动应用程序，包括检查并杀死同名进程
// ==============================================

using System;
using System.Diagnostics;
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
            // 检查并杀死同名进程
            KillExistingProcesses();

            // 启用视觉样式
            Application.EnableVisualStyles();
            // 设置默认文本呈现为兼容模式
            Application.SetCompatibleTextRenderingDefault(false);
            // 运行主窗体
            Application.Run(new Main());
        }

        /// <summary>
        /// 杀死已存在的同名进程
        /// </summary>
        private static void KillExistingProcesses()
        {
            try
            {
                // 获取当前进程名（不带扩展名）
                string currentProcessName = Process.GetCurrentProcess().ProcessName;

                // 查找所有同名进程
                Process[] processes = Process.GetProcessesByName(currentProcessName);

                foreach (Process process in processes)
                {
                    // 跳过当前进程
                    if (process.Id == Process.GetCurrentProcess().Id)
                    {
                        continue;
                    }

                    // 尝试关闭进程
                    try
                    {
                        // 先尝试正常关闭
                        process.CloseMainWindow();
                        // 等待进程退出
                        if (!process.WaitForExit(1000))
                        {
                            // 如果1秒内没有退出，强制杀死
                            process.Kill();
                        }
                    }
                    catch
                    {
                        // 如果正常关闭失败，直接强制杀死
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                            // 忽略杀死失败的情况
                        }
                    }
                }
            }
            catch
            {
                // 忽略任何错误，继续启动程序
            }
        }
    }
}