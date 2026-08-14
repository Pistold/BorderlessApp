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
                // window only hides it rather than exiting. Ask it to
                // restore its own window instead of leaving the user with
                // no way to get back to it.
                WindowHelper.RequestExistingInstanceToShow();
                return;
            }

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
