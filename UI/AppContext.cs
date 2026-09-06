using System.Windows.Forms;
using DeskPing.Core;
using Microsoft.Win32;

namespace DeskPing.UI;

/// <summary>
/// 应用生命周期：托盘常驻，窗口只是纸片。
/// 退出时统一保存设置（窗口位置、主题、模式等）。
/// </summary>
public sealed class AppContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly TimerEngine _engine;
    private readonly MainForm _form;
    private readonly TrayIcon _tray;

    public AppContext()
    {
        _settings = SettingsStore.Load();
        PaperTheme.Apply(_settings.ThemeId, _settings.Dark);

        _engine = new TimerEngine();
        _form = new MainForm(_engine, _settings, () =>
            _tray!.ShowBalloon("还在右下角托盘里安静待命，右键托盘图标可退出。"));
        _tray = new TrayIcon(
            _settings,
            toggleForm: () => _form.ToggleVisible(),
            setMode: mode => _form.SetMode(mode),
            getMode: () => _engine.Mode,
            setTheme: (id, dark) => _form.SetTheme(id, dark),
            setSound: on => _form.SetSound(on),
            setAutoStart: SetAutoStart,
            getTopMost: () => _form.Pinned,
            setTopMost: v => _form.SetPinned(v),
            exit: ExitApp);

        _engine.Alarm += OnAlarm;
        _form.ThemeChanged += () => _tray.RebuildIcon();

        _form.ShowWindow();
    }

    private void OnAlarm()
    {
        var text = _engine.Mode switch
        {
            TimerMode.CountDown => "倒计时结束，回来看看。",
            TimerMode.TargetTime => _engine.DailyRepeat ? "目标时刻到，明日此时再见。" : "目标时刻到了！",
            _ => "时间到！",
        };
        _tray.ShowBalloon(text);
        _form.TriggerAlert();
    }

    private void SetAutoStart(bool enabled)
    {
        _settings.AutoStart = enabled;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null) return;
            if (enabled) key.SetValue("DeskPing", $"\"{Application.ExecutablePath}\"");
            else key.DeleteValue("DeskPing", throwOnMissingValue: false);
        }
        catch
        {
            // 无权限时静默忽略，不影响运行。
        }
    }

    private void ExitApp()
    {
        _form.SavePositionInto(_settings);
        _settings.Mode = _engine.Mode switch
        {
            TimerMode.CountUp => "countup",
            TimerMode.TargetTime => "target",
            _ => "countdown",
        };
        _settings.CountdownSeconds = (int)_engine.Duration.TotalSeconds;
        _settings.TargetTime = _engine.Target.ToString("HH:mm");
        _settings.DailyRepeat = _engine.DailyRepeat;
        _settings.SoundOn = _engine.SoundOn;
        _settings.TopMost = _form.Pinned;
        _settings.LayoutV = 2;
        SettingsStore.Save(_settings);

        _tray.Dispose();
        _engine.Dispose();
        _form.Dispose();
        ExitThread();
    }
}
