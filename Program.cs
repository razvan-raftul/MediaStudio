using System;
using System.Net;
using System.Windows.Forms;

namespace MediaStudio
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS 1.2 și 1.3
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ShowCrash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowCrash(e.ExceptionObject as Exception);
            Settings.Load();
            Application.Run(new MainForm());
        }

        static void ShowCrash(Exception ex)
        {
            MessageBox.Show("A apărut o eroare neașteptată. Textul de mai jos se poate copia cu Control+C.\r\n\r\n" + (ex == null ? "necunoscută" : ex.ToString()),
                "Media Studio - eroare", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
