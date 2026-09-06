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
        Application.Run(new DeskPing.UI.AppContext());
    }
}
