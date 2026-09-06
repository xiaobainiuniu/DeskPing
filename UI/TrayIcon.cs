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
    private readonly Action _exit;

    private readonly NotifyIcon _notify = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _themeMenu = new("主题");
    private readonly ToolStripMenuItem _darkItem = new("深色模式");
    private readonly ToolStripMenuItem _soundItem = new("提醒音");
    private readonly ToolStripMenuItem _pinItem = new("置顶");
    private readonly ToolStripMenuItem _autoStartItem = new("开机自启");
    private readonly List<ToolStripMenuItem> _modeItems = new();

    private IntPtr _iconHandle;
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
        _exit = exit;

        _schemeId = settings.ThemeId;
        _dark = settings.Dark;

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
        var face = new RectangleF(3.5f, 3.5f, 25f, 25f);
        using (var paper = new SolidBrush(p.Paper))
        using (var ring = new Pen(p.PaperBorder, 2f))
        {
            g.FillEllipse(paper, face);
            g.DrawEllipse(ring, face);
        }

        // 12/3/6/9 时刻度
        const float cx = 16f, cy = 16f, r1 = 4.2f, r2 = 6.8f;
        using (var tick = new Pen(p.WeakText, 1.4f))
        {
            g.DrawLine(tick, cx, cy - r1, cx, cy - r2);
            g.DrawLine(tick, cx + r1, cy, cx + r2, cy);
            g.DrawLine(tick, cx, cy + r1, cx, cy + r2);
            g.DrawLine(tick, cx - r1, cy, cx - r2, cy);
        }

        // 指针（经典 10:10，最耐看的钟面形态）
        using (var hand = new Pen(p.Active, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(hand, cx, cy, cx + 5.4f, cy - 3.1f);  // 分针 → 2 点
            g.DrawLine(hand, cx, cy, cx - 3.1f, cy - 5.4f);  // 时针 → 10 点
        }
        using (var dot = new SolidBrush(p.Danger))
            g.FillEllipse(dot, cx - 1.6f, cy - 1.6f, 3.2f, 3.2f);

        return bmp;
    }

    public void RebuildIcon(string? schemeId = null, bool? dark = null)
    {
        if (schemeId != null) _schemeId = schemeId;
        if (dark != null) _dark = dark.Value;
        PaperTheme.Apply(_schemeId, _dark);

        using var bmp = CreateClockBitmap();
        var h = bmp.GetHicon();
        if (_iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
        _iconHandle = h;
        _notify.Icon = Icon.FromHandle(h);
    }

    public void ShowBalloon(string text) =>
        _notify.ShowBalloonTip(2500, "DeskPing 提醒", text, ToolTipIcon.Info);

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
        _menu.Dispose();
        if (_iconHandle != IntPtr.Zero)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = IntPtr.Zero;
        }
    }

    private void BuildMenu()
    {
        _menu.Opening += (_, _) => RefreshChecks();

        _menu.Items.Add(new ToolStripMenuItem("显示 / 隐藏", null, (_, _) => _toggleForm()));
        _menu.Items.Add(new ToolStripSeparator());

        var modes = new[] { (TimerMode.CountUp, "正计时"), (TimerMode.CountDown, "倒计时"), (TimerMode.TargetTime, "目标时刻") };
        foreach (var (mode, name) in modes)
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => _setMode(mode)) { Tag = mode };
            _modeItems.Add(item);
            _menu.Items.Add(item);
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_themeMenu);
        for (var i = 0; i < PaperTheme.All.Length; i++)
        {
            var id = PaperTheme.All[i];
            var item = new ToolStripMenuItem(PaperTheme.Names[i], null, (_, _) =>
            {
                _settings.ThemeId = id;
                _setTheme(id, _dark);
                RebuildIcon(id);
            })
            { Tag = id };
            _themeMenu.DropDownItems.Add(item);
        }

        _darkItem.Click += (_, _) =>
        {
            _dark = !_dark;
            _settings.Dark = _dark;
            _setTheme(_schemeId, _dark);
            RebuildIcon();
        };
        _menu.Items.Add(_darkItem);

        _soundItem.Click += (_, _) =>
        {
            _settings.SoundOn = !_settings.SoundOn;
            _setSound(_settings.SoundOn);
        };
        _menu.Items.Add(_soundItem);

        _pinItem.Click += (_, _) => _setTopMost(!_getTopMost());
        _menu.Items.Add(_pinItem);

        _autoStartItem.Click += (_, _) =>
        {
            _settings.AutoStart = !_settings.AutoStart;
            _setAutoStart(_settings.AutoStart);
        };
        _menu.Items.Add(_autoStartItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => _exit()));
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

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
