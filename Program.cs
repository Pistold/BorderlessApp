using System;
using System.Threading;
using System.Windows.Forms;

namespace BorderlessApp
{
    static class Program
    {
        // Unique-ish name so this doesn't collide with any other app's mutex.
        private const string MutexName = "BorderlessGameApp-SingleInstance-8F3E2C11";

        [STAThread]
        static void Main()
        {
            using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

            if (!createdNew)
            {
                // Another instance already owns the mutex - including one
                // that's minimized to the tray, since closing the main
                // window only hides it rather than exiting.
                MessageBox.Show(
                    "Borderless Window Manager is already running - check your system tray.",
                    "Already running",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
