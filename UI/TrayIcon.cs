using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DeskPing.Core;

namespace DeskPing.UI;

/// <summary>
/// 系统托盘：时钟图标运行时绘制（无需图标资源文件），
/// 右键菜单提供模式切换、主题、置顶、提醒音、开机自启与退出。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action _toggleForm;
    private readonly Action<TimerMode> _setMode;
    private readonly Func<TimerMode> _getMode;
    private readonly Action<string, bool> _setTheme;
    private readonly Action<bool> _setSound;
    private readonly Action<bool> _setAutoStart;
    private readonly Func<bool> _getTopMost;
    private readonly Action<bool> _setTopMost;
    private readonly Action<bool> _setLang;
    private readonly Action _exit;

    private readonly NotifyIcon _notify = new();
    private readonly ContextMenuStrip _menu = new();
    private ToolStripMenuItem _themeMenu = null!;
    private ToolStripMenuItem _darkItem = null!;
    private ToolStripMenuItem _soundItem = null!;
    private ToolStripMenuItem _pinItem = null!;
    private ToolStripMenuItem _autoStartItem = null!;
    private readonly List<ToolStripMenuItem> _modeItems = new();

    private Icon? _icon;
    private string _schemeId;
    private bool _dark;

    public TrayIcon(
        AppSettings settings,
        Action toggleForm,
        Action<TimerMode> setMode,
        Func<TimerMode> getMode,
        Action<string, bool> setTheme,
        Action<bool> setSound,
        Action<bool> setAutoStart,
        Func<bool> getTopMost,
        Action<bool> setTopMost,
        Action<bool> setLang,
        Action exit)
    {
        _settings = settings;
        _toggleForm = toggleForm;
        _setMode = setMode;
        _getMode = getMode;
        _setTheme = setTheme;
        _setSound = setSound;
        _setAutoStart = setAutoStart;
        _getTopMost = getTopMost;
        _setTopMost = setTopMost;
        _setLang = setLang;
        _exit = exit;

        _schemeId = settings.ThemeId;
        _dark = settings.Dark;

        _menu.Opening += (_, _) => RefreshChecks();
        BuildMenu();
        _notify.Text = "DeskPing";
        _notify.ContextMenuStrip = _menu;
        _notify.Visible = true;
        _notify.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _toggleForm();
        };
        _notify.MouseDoubleClick += (_, _) => _toggleForm();
        RebuildIcon();
    }

    /// <summary>绘制 32x32 时钟图标（托盘与任务栏共用同一套绘制代码）。</summary>
    public static Bitmap CreateClockBitmap()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var p = PaperTheme.Current;

        // 纸面圆盘
        var face = new RectangleF(2.5f, 2.5f, 27f, 27f);
        using (var paper = new SolidBrush(p.Paper))
        using (var ring = new Pen(p.PaperBorder, 1.6f))
        {
            g.FillEllipse(paper, face);
            g.DrawEllipse(ring, face);
        }

        // 12 个刻度：整点加粗，其余细点
        const float cx = 16f, cy = 16f;
        for (var i = 0; i < 12; i++)
        {
            var ang = (i * 30 - 90) * Math.PI / 180.0;
            var cos = (float)Math.Cos(ang);
            var sin = (float)Math.Sin(ang);
            var major = i % 3 == 0;
            using var tick = new Pen(major ? p.Text : p.WeakText, major ? 1.7f : 0.9f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            var r1 = major ? 8.5f : 9.5f;
            g.DrawLine(tick, cx + cos * r1, cy + sin * r1, cx + cos * 10.6f, cy + sin * 10.6f);
        }

        // 指针（经典 10:10，带配重短尾）
        using (var hour = new Pen(p.Text, 2.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(hour, cx + 1.5f, cy + 2.6f, cx - 3.4f, cy - 5.9f); // 时针 → 10 点
        using (var min = new Pen(p.Text, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(min, cx - 2.1f, cy + 1.2f, cx + 6.6f, cy - 3.8f);   // 分针 → 2 点
        using (var dot = new SolidBrush(p.Active))
            g.FillEllipse(dot, cx - 1.7f, cy - 1.7f, 3.4f, 3.4f);

        // 右上角提醒小点：一看到它就想起"到点提醒"
        using (var badge = new SolidBrush(p.Danger))
            g.FillEllipse(badge, 23.2f, 5.2f, 5.6f, 5.6f);
        using (var cut = new Pen(p.Paper, 1.6f))
            g.DrawEllipse(cut, 23.2f, 5.2f, 5.6f, 5.6f);

        return bmp;
    }

    public void RebuildIcon(string? schemeId = null, bool? dark = null)
    {
        if (schemeId != null) _schemeId = schemeId;
        if (dark != null) _dark = dark.Value;
        PaperTheme.Apply(_schemeId, _dark);

        using var bmp = CreateClockBitmap();
        var newIcon = Icon.FromHandle(bmp.GetHicon());
        _notify.Icon = newIcon;
        _icon?.Dispose(); // Icon.Dispose 会销毁其句柄
        _icon = newIcon;
    }

    public void ShowBalloon(string text) =>
        _notify.ShowBalloonTip(2500, Locale.T("DeskPing 提醒", "DeskPing Reminder"), text, ToolTipIcon.Info);

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
    }

    /// <summary>语言切换后重建菜单（菜单文案随语言即时更新）。</summary>
    private void RebuildMenu()
    {
        _menu.Items.Clear();
        _modeItems.Clear();
        BuildMenu();
    }

    private void BuildMenu()
    {
        _menu.Items.Add(new ToolStripMenuItem(Locale.T("显示 / 隐藏", "Show / Hide"), null, (_, _) => _toggleForm()));
        _menu.Items.Add(new ToolStripSeparator());

        var modes = new[]
        {
            (TimerMode.CountUp, Locale.T("正计时", "Count Up")),
            (TimerMode.CountDown, Locale.T("倒计时", "Countdown")),
            (TimerMode.TargetTime, Locale.T("目标时刻", "Target Time")),
        };
        foreach (var (mode, name) in modes)
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => _setMode(mode)) { Tag = mode };
            _modeItems.Add(item);
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new ToolStripSeparator());

        var langMenu = new ToolStripMenuItem(Locale.T("语言", "Language"));
        foreach (var (en, name) in new[] { (false, "中文"), (true, "English") })
        {
            langMenu.DropDownItems.Add(new ToolStripMenuItem(name, null, (_, _) =>
            {
                if (en == Locale.IsEnglish) return;
                _setLang(en);
                // 菜单正在收起，稍后再重建避免冲突
                _menu.BeginInvoke((Action)RebuildMenu);
            })
            { Tag = en, Checked = en == Locale.IsEnglish });
        }
        _menu.Items.Add(langMenu);

        _themeMenu = new ToolStripMenuItem(Locale.T("主题", "Theme"));
        _themeMenu.DropDownItems.Clear();
        _menu.Items.Add(_themeMenu);
        for (var i = 0; i < PaperTheme.All.Length; i++)
        {
            var id = PaperTheme.All[i];
            var item = new ToolStripMenuItem(Locale.IsEnglish ? PaperTheme.NamesEn[i] : PaperTheme.Names[i], null, (_, _) =>
            {
                _settings.ThemeId = id;
                _setTheme(id, _dark);
                RebuildIcon(id);
            })
            { Tag = id };
            _themeMenu.DropDownItems.Add(item);
        }

        _darkItem = new ToolStripMenuItem(Locale.T("深色模式", "Dark Mode"));
        _darkItem.Click += (_, _) =>
        {
            _dark = !_dark;
            _settings.Dark = _dark;
            _setTheme(_schemeId, _dark);
            RebuildIcon();
        };
        _menu.Items.Add(_darkItem);

        _soundItem = new ToolStripMenuItem(Locale.T("提醒音", "Sound"));
        _soundItem.Click += (_, _) =>
        {
            _settings.SoundOn = !_settings.SoundOn;
            _setSound(_settings.SoundOn);
        };
        _menu.Items.Add(_soundItem);

        _pinItem = new ToolStripMenuItem(Locale.T("置顶", "Always on Top"));
        _pinItem.Click += (_, _) => _setTopMost(!_getTopMost());
        _menu.Items.Add(_pinItem);

        _autoStartItem = new ToolStripMenuItem(Locale.T("开机自启", "Run at Startup"));
        _autoStartItem.Click += (_, _) =>
        {
            _settings.AutoStart = !_settings.AutoStart;
            _setAutoStart(_settings.AutoStart);
        };
        _menu.Items.Add(_autoStartItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem(Locale.T("退出", "Exit"), null, (_, _) => _exit()));
    }

    private void RefreshChecks()
    {
        foreach (var item in _modeItems)
            item.Checked = (TimerMode)item.Tag! == _getMode();
        foreach (var item in _themeMenu.DropDownItems.OfType<ToolStripMenuItem>())
            item.Checked = item.Tag as string == _schemeId;
        _darkItem.Checked = _dark;
        _soundItem.Checked = _settings.SoundOn;
        _pinItem.Checked = _getTopMost();
        _autoStartItem.Checked = _settings.AutoStart;
    }
}
