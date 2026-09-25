using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace AudioTemplates
{
    static class Program
    {
        static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, "AudioTemplates.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    // Already running: ask the existing instance to show its window.
                    PostMessage(HWND_BROADCAST, MainForm.ShowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool startMinimized = Array.Exists(args, a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
                var form = new MainForm();
                IntPtr handle = form.Handle; // create the window now so hotkeys work while hidden
                if (!startMinimized) form.Show();
                Application.Run();
                GC.KeepAlive(mutex);
            }
        }
    }
}
