using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using System.Windows.Forms;
using EcloudLite.Infrastructure;
using EcloudLite.UI;

namespace EcloudLite
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Logger.Initialize();
                string executablePath = Process.GetCurrentProcess().MainModule.FileName;
                Logger.Info("BOOT", string.Format(
                    "application start version={6} build_utc={0:O} exe={1} os={2} clr={3} x64_os={4} x64_process={5}",
                    File.GetLastWriteTimeUtc(executablePath),
                    executablePath,
                    Environment.OSVersion,
                    Environment.Version,
                    Environment.Is64BitOperatingSystem,
                    Environment.Is64BitProcess,
                    AppInfo.LiteVersion));

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += ThreadException;
                AppDomain.CurrentDomain.UnhandledException += DomainException;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                Logger.Info("BOOT", "application exit");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("BOOT_FATAL_TYPE=" + exception.GetType().FullName);
                Console.Error.WriteLine("BOOT_FATAL_MESSAGE=" + (exception.Message ?? string.Empty));
                Console.Error.WriteLine("BOOT_FATAL_STACK=" + (exception.StackTrace ?? "<no stack>"));
                try { Logger.Exception("FATAL", exception, "top-level startup failure"); } catch { }
                Environment.ExitCode = 1;
            }
        }

        private static void ThreadException(object sender, ThreadExceptionEventArgs e)
        {
            Logger.Exception("FATAL", e.Exception, "unhandled UI exception");
            MessageBox.Show("程序发生错误，详细信息已写入日志。", "Ecloud Lite", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static void DomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception exception = e.ExceptionObject as Exception;
            if (exception != null) Logger.Exception("FATAL", exception, "unhandled domain exception");
            else Logger.Error("FATAL", "unhandled non-Exception object");
        }
    }
}
