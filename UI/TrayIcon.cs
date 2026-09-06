using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DeskPing.Core;

namespace DeskPing.UI;

/// <summary>
/// 系统托盘：纸张风图标运行时绘制（无需图标资源文件），
/// 右键菜单提供模式切换、主题、提醒音、开机自启与退出。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action _toggleForm;
    private readonly Action<TimerMode> _setMode;
    private readonly Action<string, bool> _setTheme;
    private readonly Action<bool> _setSound;
    private readonly Action<bool> _setAutoStart;
    private readonly Func<TimerMode> _getMode;
    private readonly Action _exit;

    private readonly NotifyIcon _notify = new();
    private readonly ContextMenuStrip _menu = new();
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
        Action exit)
    {
        _settings = settings;
        _toggleForm = toggleForm;
        _setMode = setMode;
        _getMode = getMode;
        _setTheme = setTheme;
        _setSound = setSound;
        _setAutoStart = setAutoStart;
        _exit = exit;

        _schemeId = settings.ThemeId;
        _dark = settings.Dark;

        BuildMenu();
        _notify.Text = "DeskPing · 一张纸提醒";
        _notify.ContextMenuStrip = _menu;
        _notify.Visible = true;
        _notify.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _toggleForm();
        };
        _notify.MouseDoubleClick += (_, _) => _toggleForm();
        RebuildIcon();
    }

    public void RebuildIcon(string? schemeId = null, bool? dark = null)
    {
        if (schemeId != null) _schemeId = schemeId;
        if (dark != null) _dark = dark.Value;
        PaperTheme.Apply(_schemeId, _dark);

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var p = PaperTheme.Current;

            // 纸片底：暖纸色圆角矩形 + 描边
            using (var path = RoundRect(new RectangleF(3, 5, 26, 23), 6))
            using (var paper = new SolidBrush(p.Paper))
            using (var border = new Pen(p.PaperBorder, 1.6f))
            {
                g.FillPath(paper, path);
                g.DrawPath(border, path);
            }

            // 钟面：墨色圆环 + 指针（指向右上，寓意"时间提醒"）
            var cx = 16f;
            var cy = 16f;
            using (var ring = new Pen(p.Text, 2f))
                g.DrawEllipse(ring, cx - 7, cy - 7, 14, 14);
            using (var hand = new Pen(p.Active, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(hand, cx, cy, cx + 3.2f, cy - 3.2f); // 分针指向 1 点方向
                g.DrawLine(hand, cx, cy, cx - 3.6f, cy + 2.6f); // 时针指向 9 点方向
            }
        }

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
        _menu.Items.Add(new ToolStripMenuItem("显示 / 隐藏纸片", null, (_, _) => _toggleForm()));
        _menu.Items.Add(new ToolStripSeparator());

        // 模式（radio）
        var modes = new[] { (TimerMode.CountUp, "正计时"), (TimerMode.CountDown, "倒计时"), (TimerMode.TargetTime, "目标时刻") };
        foreach (var (mode, name) in modes)
        {
            var item = new ToolStripMenuItem(name, null, (_, _) => _setMode(mode)) { CheckOnClick = false };
            item.Click += (_, _) => RefreshModeChecks();
            item.Tag = mode;
            _menu.Items.Add(item);
        }
        RefreshModeChecks();

        _menu.Items.Add(new ToolStripSeparator());

        // 主题子菜单（radio）
        var themeMenu = new ToolStripMenuItem("主题");
        for (var i = 0; i < PaperTheme.All.Length; i++)
        {
            var id = PaperTheme.All[i];
            var item = new ToolStripMenuItem(PaperTheme.Names[i], null, (_, _) =>
            {
                _settings.ThemeId = id;
                _setTheme(id, _dark);
                RebuildIcon();
                RefreshThemeChecks(themeMenu);
            })
            { Tag = id };
            themeMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(themeMenu);
        RefreshThemeChecks(themeMenu);

        var darkItem = new ToolStripMenuItem("深色模式", null, (_, _) =>
        {
            _dark = !_dark;
            _settings.Dark = _dark;
            _setTheme(_schemeId, _dark);
            RebuildIcon();
        })
        { Checked = _dark };
        _menu.Items.Add(darkItem);

        var soundItem = new ToolStripMenuItem("提醒音", null, (_, _) =>
        {
            _settings.SoundOn = !_settings.SoundOn;
            _setSound(_settings.SoundOn);
        })
        { Checked = _settings.SoundOn };
        _menu.Items.Add(soundItem);

        var autoStartItem = new ToolStripMenuItem("开机自启", null, (_, _) =>
        {
            _settings.AutoStart = !_settings.AutoStart;
            _setAutoStart(_settings.AutoStart);
        })
        { Checked = _settings.AutoStart };
        _menu.Items.Add(autoStartItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => _exit()));
    }

    private void RefreshModeChecks()
    {
        foreach (var item in _menu.Items.OfType<ToolStripMenuItem>().Where(i => i.Tag is TimerMode))
            item.Checked = (TimerMode)item.Tag! == _getMode();
    }

    private void RefreshThemeChecks(ToolStripMenuItem themeMenu)
    {
        foreach (var item in themeMenu.DropDownItems.OfType<ToolStripMenuItem>())
            item.Checked = item.Tag as string == _schemeId;
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
