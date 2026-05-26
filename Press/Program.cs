using System;
using System.Threading;
using System.Windows.Forms;

namespace Press
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Console monitor chạy nền
            var consoleThread = new Thread(() =>
            {
                while (true) Thread.Sleep(1000);
            });
            consoleThread.IsBackground = true;
            consoleThread.Start();

            Application.Run(new Home());
        }
    }
}