using System.Threading;
using System.Windows.Forms;
using DeskPing.UI;

namespace DeskPing;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 单实例：避免重复启动造成双重提醒。
        using var mutex = new Mutex(true, @"Local\DeskPing.SingleInstance", out var isFirst);
        if (!isFirst) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 全局异常：写入日志（下次崩溃时能看到完整堆栈），弹一次提示框后继续运行
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        var notified = false;
        Application.ThreadException += (_, e) =>
        {
            LogCrash(e.Exception);
            if (!notified)
            {
                notified = true;
                MessageBox.Show(
                    "DeskPing 遇到了一个错误，详情已写入：\r\n" + CrashLogPath +
                    "\r\n\r\n程序将继续运行。",
                    "DeskPing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);

        Application.Run(new DeskPing.UI.AppContext());
    }

    private static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeskPing", "error.log");

    private static void LogCrash(Exception? ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(CrashLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\r\n\r\n");
        }
        catch
        {
            // 日志写不进也不影响运行。
        }
    }
}
