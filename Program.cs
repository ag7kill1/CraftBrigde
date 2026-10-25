using System;
using System.IO;
using System.Windows.Forms;

namespace CraftBridge
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
            { MessageBox.Show("遇到一个问题，请保留当前迁移包后重试。\r\n\r\n" + e.Exception.Message, "CraftBridge", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            string source = "", archive = "";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--source" && i + 1 < args.Length) source = args[++i];
                else if (args[i] == "--import" && i + 1 < args.Length) archive = args[++i];
                else if (args.Length == 1 && File.Exists(args[i]) && args[i].EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) archive = args[i];
            }
            try { Application.Run(new AppForm(source, archive)); }
            catch (Exception e)
            { MessageBox.Show("程序暂时无法继续。\r\n\r\n" + e.Message, "CraftBridge", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
