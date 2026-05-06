using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace Demo
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            KillExistingProcesses();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Main());
        }

        private static void KillExistingProcesses()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var currentProcessName = currentProcess.ProcessName;
                var processes = Process.GetProcessesByName(currentProcessName);

                foreach (var process in processes)
                {
                    if (process.Id == currentProcess.Id)
                    {
                        continue;
                    }

                    try
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(1000))
                        {
                            process.Kill();
                        }
                    }
                    catch
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}
