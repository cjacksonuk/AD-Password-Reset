using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("AD User Manager")]
[assembly: System.Reflection.AssemblyDescription("Create Active Directory users and reset passwords within a defined OU scope")]
[assembly: System.Reflection.AssemblyCompany("ICT Hero - support@icthero.co.uk")]
[assembly: System.Reflection.AssemblyProduct("AD User Manager")]
[assembly: System.Reflection.AssemblyCopyright("Developed by support@icthero.co.uk 11/11/26")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace ADUserManager
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("/selftest", StringComparison.OrdinalIgnoreCase))
                return SelfTest.Run(args);

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                Theme.Scale = g.DpiX / 96f;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(AdService.Friendly(e.Exception), "AD User Manager - unexpected error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            Application.Run(new MainForm());
            return 0;
        }
    }
}
