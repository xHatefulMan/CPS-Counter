using System;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.IO;

namespace CompteurCPS
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [STAThread]
        static void Main()
        {
            bool createdNew;
            using var mutex = new Mutex(true, "CompteurCPS_SingleInstance", out createdNew);

            if (!createdNew)
            {
                var current = System.Diagnostics.Process.GetCurrentProcess();
                var procs = System.Diagnostics.Process.GetProcessesByName(current.ProcessName);
                foreach (var p in procs)
                {
                    if (p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero)
                    {
                        ShowWindow(p.MainWindowHandle, 9);
                        SetForegroundWindow(p.MainWindowHandle);
                    }
                }
                var flagPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "open_settings.flag");
                File.WriteAllText(flagPath, "1");
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}