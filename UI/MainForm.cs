using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;
using System.Runtime.InteropServices;
using DeskPing.Core;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace DeskPing.UI;

/// <summary>
/// 纸张主窗口：全自绘、无边框圆角纸片 + 系统阴影。
/// 所有绘制按需进行——空闲时 CPU 为零，是功耗控制的核心。
/// </summary>
public sealed class MainForm : Form
{
    // ---- 布局常量（逻辑像素，随 DPI 自动缩放）----
    private const int W = 384, H = 312;
    private const int TitleBarH = 38, TabH = 36;
    private const int SettingsY = 180, SettingsH = 46;
    private const int StatusY = 282;
    private static readonly Rectangle TimeRect = new(0, 74, W, 106);

    // 设置行控件坐标（与下方 HitTest / 绘制保持一致）
    private static readonly Rectangle RCountH = new(47, 191, 34, 24);
    private static readonly Rectangle RCountM = new(91, 191, 34, 24);
    private static readonly Rectangle RCountS = new(135, 191, 34, 24);
    private static readonly Rectangle RTargetH = new(47, 191, 34, 24);
    private static readonly Rectangle RTargetM = new(91, 191, 34, 24);
    private static readonly Rectangle[] RPresets = { new(175, 191, 44, 24), new(225, 191, 44, 24), new(275, 191, 44, 24) };
    private static readonly Rectangle RToday = new(131, 191, 44, 24);
    private static readonly Rectangle RTomorrow = new(181, 191, 44, 24);
    private static readonly Rectangle RPick = new(231, 191, 44, 24);
    private static readonly Rectangle RDaily = new(285, 191, 54, 24);

    private readonly TimerEngine _engine;
    private readonly AppSettings _settings;
    private readonly Action _onFirstHide;

    private readonly TextBox _txtH = MakeInput(RCountH);
    private readonly TextBox _txtM = MakeInput(RCountM);
    private readonly TextBox _txtS = MakeInput(RCountS);
    private readonly TextBox _txtTH = MakeInput(RTargetH);
    private readonly TextBox _txtTM = MakeInput(RTargetM);

    private readonly WinFormsTimer _alertTimer;

    private DateTime _targetDate = DateTime.Today;
    private bool _targetDateIsCustom;

    private bool _alerting;
    private double _animPhase;
    private int _soundCountdown;
    private bool _flashActive;
    private bool _everHidden;

    private Zone _hover = Zone.None;
    private bool _syncingInputs;

    // 字体缓存（避免重复创建 GDI 对象）
    private Font _fTitle = null!, _fTab = null!, _fBig = null!, _fBigAlert = null!, _fSmall = null!, _fBtn = null!;

    public event Action? ThemeChanged;

    public MainForm(TimerEngine engine, AppSettings settings, Action onFirstHide)
    {
        _engine = engine;
        _settings = settings;
        _onFirstHide = onFirstHide;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(W, H);
        ShowInTaskbar = true;
        DoubleBuffered = true;
        KeyPreview = true;

        MakeFonts();
        SetupInputs();
        ApplySettingsFromStore();
        ApplyTheme();

        // 初始位置：优先恢复上次位置，否则桌面右下角。
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, W, H);
        var x = settings.WindowX >= 0 ? settings.WindowX : wa.Right - W - 48;
        var y = settings.WindowY >= 0 ? settings.WindowY : wa.Bottom - H - 64;
        Location = new Point(Math.Clamp(x, wa.Left - W + 80, wa.Right - 80), Math.Clamp(y, wa.Top, wa.Bottom - 60));

        // 提醒动画节拍：仅提醒期间运行（25fps），平时完全停止。
        _alertTimer = new WinFormsTimer { Interval = 40 };
        _alertTimer.Tick += OnAlertTick;

        _engine.Ticked += OnEngineTick;
        _engine.StateChanged += OnEngineStateChanged;
    }

    private static TextBox MakeInput(Rectangle r)
    {
        return new TextBox
        {
            Bounds = r,
            BorderStyle = BorderStyle.None,
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 2,
            Font = new Font(PaperTheme.FontName, 9.5f),
            TabStop = true,
            Visible = false,
        };
    }

    private void MakeFonts()
    {
        var p = PaperTheme.Current;
        _fTitle = new Font(PaperTheme.FontName, 9.5f);
        _fTab = new Font(PaperTheme.FontName, 9.5f);
        _fBig = new Font(PaperTheme.FontName, 40f, FontStyle.Regular);
        _fBigAlert = new Font(PaperTheme.FontName, 30f, FontStyle.Bold);
        _fSmall = new Font(PaperTheme.FontName, 8.5f);
        _fBtn = new Font(PaperTheme.FontName, 10f, FontStyle.Bold);
        _ = p;
    }

    private void SetupInputs()
    {
        _txtH.TextChanged += (_, _) => ApplyCountdownInputs();
        _txtM.TextChanged += (_, _) => ApplyCountdownInputs();
        _txtS.TextChanged += (_, _) => ApplyCountdownInputs();
        _txtTH.TextChanged += (_, _) => ApplyTargetInputs();
        _txtTM.TextChanged += (_, _) => ApplyTargetInputs();

        foreach (var tb in new[] { _txtH, _txtM, _txtS, _txtTH, _txtTM })
        {
            tb.KeyPress += (_, e) => e.Handled = !char.IsDigit(e.KeyChar) && e.KeyChar != '\b';
            tb.Enter += (_, _) => Invalidate(SettingsBounds());
            tb.Leave += (_, _) => Invalidate(SettingsBounds());
            Controls.Add(tb);
        }
    }

    private void ApplySettingsFromStore()
    {
        _engine.SoundOn = _settings.SoundOn;
        _engine.DailyRepeat = _settings.DailyRepeat;

        _engine.SetMode(_settings.Mode switch
        {
            "countup" => TimerMode.CountUp,
            "target" => TimerMode.TargetTime,
            _ => TimerMode.CountDown,
        });

        _engine.SetCountdown(TimeSpan.FromSeconds(Math.Clamp(_settings.CountdownSeconds, 1, 99 * 3600 + 59 * 60 + 59)));

        if (TimeSpan.TryParse(_settings.TargetTime, out var tt))
        {
            _targetDate = DateTime.Today;
            _targetDateIsCustom = false;
            _engine.SetTarget(_targetDate.Add(tt), _settings.DailyRepeat);
        }
    }

    // ---------- 对外操作（托盘菜单等调用） ----------

    public void SetMode(TimerMode mode)
    {
        StopAlert();
        _engine.SetMode(mode);
        UpdateModeUi();
        Invalidate();
    }

    public void SetTheme(string schemeId, bool dark)
    {
        PaperTheme.Apply(schemeId, dark);
        ApplyTheme();
        ThemeChanged?.Invoke();
    }

    public void SetSound(bool on) => _engine.SoundOn = on;

    public void ToggleVisible()
    {
        if (Visible && WindowState != FormWindowState.Minimized) HideToTray();
        else ShowWindow();
    }

    public void ShowWindow()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        Invalidate();
    }

    public void SavePositionInto(AppSettings s)
    {
        s.WindowX = Left;
        s.WindowY = Top;
    }

    public void TriggerAlert()
    {
        if (_alerting) return;
        _alerting = true;
        _animPhase = 0;
        _soundCountdown = 0;

        if (!Visible || WindowState == FormWindowState.Minimized) ShowWindow();
        TopMost = true; // 提醒期间保证可见
        Activate();

        FlashWindow(true);
        if (_engine.SoundOn) SystemSounds.Exclamation.Play();
        _alertTimer.Start();
        Invalidate();
    }

    public void StopAlert()
    {
        if (!_alerting) return;
        _alerting = false;
        _alertTimer.Stop();
        FlashWindow(false);
        TopMost = false;
        _engine.Reset();
        Invalidate();
    }

    private void HideToTray()
    {
        Hide();
        if (!_everHidden)
        {
            _everHidden = true;
            _onFirstHide();
        }
    }

    // ---------- 引擎事件 ----------

    private void OnEngineTick()
    {
        if (Visible) Invalidate(TimeRect); // 窗口隐藏时零刷新
    }

    private void OnEngineStateChanged()
    {
        UpdateInputsFromEngine();
        if (Visible) Invalidate();
    }

    // ---------- 提醒动画 ----------

    private void OnAlertTick(object? sender, EventArgs e)
    {
        _animPhase += 0.16;
        Invalidate(TimeRect);
        Invalidate(new Rectangle(0, 0, W, TitleBarH)); // 边框呼吸

        _soundCountdown++;
        if (_engine.SoundOn && _soundCountdown % 100 == 0) // 每 4 秒一声
            SystemSounds.Exclamation.Play();
    }

    private void FlashWindow(bool start)
    {
        _flashActive = start;
        var fw = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = Handle,
            dwFlags = start ? FLASHW_ALL | FLASHW_TIMER : FLASHW_STOP,
            uCount = 0,
            dwTimeout = 0,
        };
        FlashWindowEx(ref fw);
    }

    // ---------- 输入解析 ----------

    private void ApplyCountdownInputs()
    {
        if (_syncingInputs) return;
        if (!int.TryParse(_txtH.Text, out var h) || !int.TryParse(_txtM.Text, out var m) || !int.TryParse(_txtS.Text, out var s)) return;
        var ts = TimeSpan.FromSeconds(h * 3600 + m * 60 + s);
        if (ts.TotalSeconds != _engine.Duration.TotalSeconds)
            _engine.SetCountdown(ts);
    }

    private void ApplyTargetInputs()
    {
        if (_syncingInputs) return;
        if (!int.TryParse(_txtTH.Text, out var h) || !int.TryParse(_txtTM.Text, out var m)) return;
        if (h > 23 || m > 59) return;
        var target = _targetDate.Date.AddHours(h).AddMinutes(m);
        if (target != _engine.Target || _settings.DailyRepeat != _engine.DailyRepeat)
            _engine.SetTarget(target, _engine.DailyRepeat);
    }

    private void UpdateInputsFromEngine()
    {
        _syncingInputs = true;
        try
        {
            var d = _engine.Duration;
            _txtH.Text = d.Hours.ToString();
            _txtM.Text = d.Minutes.ToString("D2");
            _txtS.Text = d.Seconds.ToString("D2");

            var t = _engine.Target;
            _txtTH.Text = t.Hour.ToString("D2");
            _txtTM.Text = t.Minute.ToString("D2");

            _targetDate = t.Date;
            _targetDateIsCustom = t.Date != DateTime.Today && t.Date != DateTime.Today.AddDays(1);
        }
        finally
        {
            _syncingInputs = false;
        }
    }

    private void UpdateModeUi()
    {
        var mode = _engine.Mode;
        _txtH.Visible = _txtM.Visible = _txtS.Visible = mode == TimerMode.CountDown;
        _txtTH.Visible = _txtTM.Visible = mode == TimerMode.TargetTime;
        UpdateInputsFromEngine();
        Invalidate();
    }

    // ---------- 主题 ----------

    private void ApplyTheme()
    {
        var p = PaperTheme.Current;
        BackColor = p.Paper;
        ForeColor = p.Text;
        foreach (var tb in new[] { _txtH, _txtM, _txtS, _txtTH, _txtTM })
        {
            tb.BackColor = p.Paper;
            tb.ForeColor = p.Text;
        }
        UpdateRoundedRegion();
        Invalidate();
    }

    private void UpdateRoundedRegion()
    {
        using var path = RoundedRectPath(new Rectangle(0, 0, W, H), PaperTheme.CornerRadius);
        Region = new Region(path);
    }

    // ---------- 绘制 ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var p = PaperTheme.Current;

        using (var bg = new SolidBrush(p.Paper))
            g.FillRectangle(bg, 0, 0, W, H);

        DrawChrome(g, p);
        DrawTitleBar(g, p);
        DrawTabs(g, p);
        DrawTime(g, p);
        DrawSettings(g, p);
        DrawButtons(g, p);
        DrawStatus(g, p);
    }

    /// <summary>纸片描边 + 分隔线。提醒时描边转为警示色。</summary>
    private void DrawChrome(Graphics g, Palette p)
    {
        if (_alerting)
        {
            // 呼吸边框：警示色与纸面描边之间按相位往返
            var k = (Math.Sin(_animPhase) + 1) / 2;
            var border = Blend(p.PaperBorder, p.Danger, k);
            using var pen = new Pen(border, 2f);
            using var path = RoundedRectPath(new Rectangle(1, 1, W - 3, H - 3), PaperTheme.CornerRadius);
            g.DrawPath(pen, path);
        }
        else
        {
            using var pen = new Pen(p.PaperBorder, PaperTheme.BorderWidth);
            using var path = RoundedRectPath(new Rectangle(1, 1, W - 3, H - 3), PaperTheme.CornerRadius);
            g.DrawPath(pen, path);
        }

        using var line = new Pen(PaperTheme.WithAlpha(p.PaperBorder, 110), 1f);
        g.DrawLine(line, 14, TitleBarH, W - 14, TitleBarH);
        g.DrawLine(line, 14, TitleBarH + TabH, W - 14, TitleBarH + TabH);
    }

    private void DrawTitleBar(Graphics g, Palette p)
    {
        using (var brush = new SolidBrush(p.Text))
        using (var weak = new SolidBrush(p.WeakText))
        {
            g.DrawString("DeskPing", _fTitle, brush, 16, (TitleBarH - _fTitle.Height) / 2f + 1);
            g.DrawString("· 一张纸提醒", _fTitle, weak, 16 + TextWidth(g, "DeskPing", _fTitle) + 6, (TitleBarH - _fTitle.Height) / 2f + 1);
        }

        DrawTitleButton(g, p, new Rectangle(W - 76, 7, 30, 24), "−", _hover == Zone.Minimize);
        DrawTitleButton(g, p, new Rectangle(W - 40, 7, 28, 24), "✕", _hover == Zone.Close);
    }

    private void DrawTitleButton(Graphics g, Palette p, Rectangle r, string glyph, bool hover)
    {
        using var path = RoundedRectPath(r, 6);
        if (hover)
        {
            using var b = new SolidBrush(PaperTheme.HoverTint());
            g.FillPath(b, path);
        }
        using (var pen = new Pen(p.Text, 1.4f))
        {
            var cx = r.X + r.Width / 2f;
            var cy = r.Y + r.Height / 2f;
            if (glyph == "−") g.DrawLine(pen, cx - 5, cy, cx + 5, cy);
            else
            {
                g.DrawLine(pen, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
                g.DrawLine(pen, cx - 4.5f, cy + 4.5f, cx + 4.5f, cy - 4.5f);
            }
        }
    }

    private void DrawTabs(Graphics g, Palette p)
    {
        var modes = new[] { TimerMode.CountUp, TimerMode.CountDown, TimerMode.TargetTime };
        var names = new[] { "正计时", "倒计时", "目标时刻" };
        for (var i = 0; i < 3; i++)
        {
            var r = new Rectangle(i * (W / 3), TitleBarH, W / 3, TabH);
            var zone = i == 0 ? Zone.Tab1 : i == 1 ? Zone.Tab2 : Zone.Tab3;
            var hover = _hover == zone;
            var active = _engine.Mode == modes[i];

            using (var path = RoundedRectPath(new Rectangle(r.X + 10, r.Y + 3, r.Width - 20, r.Height - 6), 7))
            {
                if (hover && !active)
                {
                    using var b = new SolidBrush(PaperTheme.HoverTint());
                    g.FillPath(b, path);
                }
            }

            using (var brush = new SolidBrush(active ? p.Text : p.WeakText))
            {
                var text = names[i];
                var w = TextWidth(g, text, _fTab);
                g.DrawString(text, _fTab, brush, r.X + (r.Width - w) / 2f, r.Y + (r.Height - _fTab.Height) / 2f + 1);
            }

            if (active)
            {
                using var brush = new SolidBrush(p.Active);
                var w = TextWidth(g, names[i], _fTab);
                g.FillRoundedRectangle(brush, new RectangleF(r.X + (r.Width - Math.Max(28, w + 10)) / 2f, r.Bottom - 5, Math.Max(28, w + 10), 3), 1.5f);
            }
        }
    }

    private void DrawTime(Graphics g, Palette p)
    {
        var text = DisplayText();
        var font = _alerting ? _fBigAlert : _fBig;
        var color = _alerting ? p.Danger : p.Text;
        var w = TextWidth(g, text, font);
        var baseY = TimeRect.Y + (TimeRect.Height - font.Height) / 2f + 4;
        var y = baseY + (_alerting ? (float)(Math.Sin(_animPhase) * 6) : 0);

        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, (W - w) / 2f, y);

        // 目标时刻模式：数字下方给一行小字提示剩余语义
        if (_engine.Mode == TimerMode.TargetTime && !_alerting && _engine.State == RunState.Running)
        {
            using var weak = new SolidBrush(p.WeakText);
            var hint = "剩余时间";
            var hw = TextWidth(g, hint, _fSmall);
            g.DrawString(hint, _fSmall, weak, (W - hw) / 2f, TimeRect.Bottom - _fSmall.Height - 8);
        }
    }

    private void DrawSettings(Graphics g, Palette p)
    {
        switch (_engine.Mode)
        {
            case TimerMode.CountDown:
                DrawCountdownSettings(g, p);
                break;
            case TimerMode.TargetTime:
                DrawTargetSettings(g, p);
                break;
            default:
                using (var weak = new SolidBrush(p.WeakText))
                {
                    var hint = "从零开始累计 · 专注当下";
                    var w = TextWidth(g, hint, _fSmall);
                    g.DrawString(hint, _fSmall, weak, (W - w) / 2f, SettingsY + (SettingsH - _fSmall.Height) / 2f);
                }
                break;
        }
    }

    private void DrawCountdownSettings(Graphics g, Palette p)
    {
        using var weak = new SolidBrush(p.WeakText);
        // 冒号
        g.DrawString(":", _fSmall, weak, 85, SettingsY + (SettingsH - _fSmall.Height) / 2f - 2);
        g.DrawString(":", _fSmall, weak, 129, SettingsY + (SettingsH - _fSmall.Height) / 2f - 2);

        DrawInputUnderline(g, p, _txtH);
        DrawInputUnderline(g, p, _txtM);
        DrawInputUnderline(g, p, _txtS);

        var presets = new[] { "+5分", "+15分", "+30分" };
        for (var i = 0; i < 3; i++)
        {
            var zone = i == 0 ? Zone.P1 : i == 1 ? Zone.P2 : Zone.P3;
            DrawChipButton(g, p, RPresets[i], presets[i], _hover == zone, false, _fSmall);
        }
    }

    private void DrawTargetSettings(Graphics g, Palette p)
    {
        using var weak = new SolidBrush(p.WeakText);
        g.DrawString(":", _fSmall, weak, 85, SettingsY + (SettingsH - _fSmall.Height) / 2f - 2);

        DrawInputUnderline(g, p, _txtTH);
        DrawInputUnderline(g, p, _txtTM);

        var todaySel = !_targetDateIsCustom && _targetDate == DateTime.Today;
        var tomorrowSel = !_targetDateIsCustom && _targetDate == DateTime.Today.AddDays(1);
        DrawChipButton(g, p, RToday, "今天", _hover == Zone.Today, todaySel, _fSmall);
        DrawChipButton(g, p, RTomorrow, "明天", _hover == Zone.Tomorrow, tomorrowSel, _fSmall);
        DrawChipButton(g, p, RPick, "…", _hover == Zone.Pick, _targetDateIsCustom, _fSmall);

        // 每日重复
        var checkRect = new Rectangle(RDaily.X, RDaily.Y + 5, 14, 14);
        var checkHover = _hover == Zone.Daily;
        using (var path = RoundedRectPath(checkRect, 3))
        {
            using var pen = new Pen(_engine.DailyRepeat ? p.Active : p.WeakText, 1.2f);
            g.DrawPath(pen, path);
            if (_engine.DailyRepeat)
            {
                using var active = new SolidBrush(p.Active);
                g.DrawLine(new Pen(active, 1.6f), checkRect.X + 3.5f, checkRect.Y + 7f, checkRect.X + 6.5f, checkRect.Y + 10f);
                g.DrawLine(new Pen(active, 1.6f), checkRect.X + 6.5f, checkRect.Y + 10f, checkRect.X + 11f, checkRect.Y + 4f);
            }
            if (checkHover)
            {
                using var b = new SolidBrush(PaperTheme.HoverTint());
                g.FillPath(b, path);
            }
        }
        using (var brush = new SolidBrush(_engine.DailyRepeat ? p.Text : p.WeakText))
            g.DrawString("每日", _fSmall, brush, RDaily.X + 18, RDaily.Y + (RDaily.Height - _fSmall.Height) / 2f + 2);
    }

    private void DrawInputUnderline(Graphics g, Palette p, TextBox tb)
    {
        using var pen = new Pen(tb.Focused ? p.Active : p.WeakText, tb.Focused ? 2f : 1f);
        g.DrawLine(pen, tb.Left, tb.Bottom + 3, tb.Right, tb.Bottom + 3);
    }

    private void DrawChipButton(Graphics g, Palette p, Rectangle r, string text, bool hover, bool selected, Font font)
    {
        using var path = RoundedRectPath(r, PaperTheme.ControlRadius - 2);
        if (selected)
        {
            using var b = new SolidBrush(p.Code);
            g.FillPath(b, path);
        }
        else if (hover)
        {
            using var b = new SolidBrush(PaperTheme.HoverTint());
            g.FillPath(b, path);
        }
        using (var pen = new Pen(selected ? p.PaperBorder : PaperTheme.WithAlpha(p.PaperBorder, 160), 1f))
            g.DrawPath(pen, path);
        using (var brush = new SolidBrush(selected ? p.Text : p.WeakText))
        {
            var w = TextWidth(g, text, font);
            g.DrawString(text, font, brush, r.X + (r.Width - w) / 2f, r.Y + (r.Height - font.Height) / 2f + 1);
        }
    }

    private void DrawButtons(Graphics g, Palette p)
    {
        var mainRect = new Rectangle(_alerting ? (W - 150) / 2 : (W - 128) / 2, 232, _alerting ? 150 : 128, 38);
        var resetRect = new Rectangle(mainRect.X - 76, 232, 64, 38);

        if (!_alerting)
            DrawOutlineButton(g, p, resetRect, "重置", _hover == Zone.Reset);

        var mainText = _alerting ? "知道了" : _engine.State == RunState.Running ? "暂停" : "开始";
        var mainColor = _alerting ? p.Danger : p.Active;
        DrawFilledButton(g, p, mainRect, mainText, _hover == Zone.Main, mainColor);
    }

    private void DrawFilledButton(Graphics g, Palette p, Rectangle r, string text, bool hover, Color fill)
    {
        using var path = RoundedRectPath(r, 10);
        using (var b = new SolidBrush(hover ? PaperTheme.Darken(fill, 0.12) : fill))
            g.FillPath(b, path);
        using (var brush = new SolidBrush(p.Paper))
        {
            var w = TextWidth(g, text, _fBtn);
            g.DrawString(text, _fBtn, brush, r.X + (r.Width - w) / 2f, r.Y + (r.Height - _fBtn.Height) / 2f + 1);
        }
    }

    private void DrawOutlineButton(Graphics g, Palette p, Rectangle r, string text, bool hover)
    {
        using var path = RoundedRectPath(r, 10);
        if (hover)
        {
            using var b = new SolidBrush(PaperTheme.HoverTint());
            g.FillPath(b, path);
        }
        using var pen = new Pen(PaperTheme.WithAlpha(p.WeakText, 180), 1f);
        g.DrawPath(pen, path);
        using (var brush = new SolidBrush(p.Text))
        {
            var w = TextWidth(g, text, _fBtn);
            g.DrawString(text, _fBtn, brush, r.X + (r.Width - w) / 2f, r.Y + (r.Height - _fBtn.Height) / 2f + 1);
        }
    }

    private void DrawStatus(Graphics g, Palette p)
    {
        using var brush = new SolidBrush(_alerting ? p.Danger : p.WeakText);
        var text = StatusText();
        var w = TextWidth(g, text, _fSmall);
        g.DrawString(text, _fSmall, brush, (W - w) / 2f, StatusY);
    }

    // ---------- 文案 ----------

    private string DisplayText()
    {
        if (_alerting) return "时间到！";
        return _engine.Mode switch
        {
            TimerMode.CountUp => Fmt(_engine.Elapsed),
            TimerMode.CountDown => Fmt(NonNeg(_engine.DisplayValue)),
            _ => _engine.State == RunState.Stopped
                ? _engine.Target.ToString("HH:mm:ss")
                : Fmt(NonNeg(_engine.DisplayValue)),
        };
    }

    private static string Fmt(TimeSpan t)
    {
        var h = (long)t.TotalHours;
        return $"{h:D2}:{t.Minutes:D2}:{t.Seconds:D2}";
    }

    private static TimeSpan NonNeg(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    private string StatusText()
    {
        if (_alerting) return "点「知道了」停止提醒";
        return (_engine.Mode, _engine.State) switch
        {
            (TimerMode.CountUp, RunState.Stopped) => "点开始 · 累计专注时长",
            (TimerMode.CountUp, RunState.Running) => "正计时中 · 享受当下",
            (TimerMode.CountUp, RunState.Paused) => "已暂停 · 点开始继续",
            (TimerMode.CountDown, RunState.Stopped) => "点开始 · 结束即提醒",
            (TimerMode.CountDown, RunState.Running) => "倒计时中",
            (TimerMode.CountDown, RunState.Paused) => "已暂停 · 点开始继续",
            (TimerMode.CountDown, RunState.Alerted) => "倒计时结束！",
            (TimerMode.TargetTime, RunState.Stopped) => $"目标：{DescribeTarget()}",
            (TimerMode.TargetTime, RunState.Running) => $"将在 {DescribeTarget()} 提醒",
            (TimerMode.TargetTime, RunState.Paused) => "已暂停 · 点开始继续",
            (TimerMode.TargetTime, RunState.Alerted) => "目标时刻已到！",
            _ => "",
        };
    }

    private string DescribeTarget()
    {
        var t = _engine.Target;
        var day = t.Date == DateTime.Today ? "今天"
            : t.Date == DateTime.Today.AddDays(1) ? "明天"
            : t.ToString("M月d日");
        return $"{day} {t:HH:mm}" + (_engine.DailyRepeat ? " · 每日" : "");
    }

    // ---------- 鼠标交互 ----------

    private enum Zone { None, Close, Minimize, Tab1, Tab2, Tab3, Main, Reset, P1, P2, P3, Today, Tomorrow, Pick, Daily }

    private Zone HitTest(Point pt)
    {
        var mode = _engine.Mode;
        if (new Rectangle(W - 40, 7, 28, 24).Contains(pt)) return Zone.Close;
        if (new Rectangle(W - 76, 7, 30, 24).Contains(pt)) return Zone.Minimize;
        if (pt.Y >= TitleBarH && pt.Y < TitleBarH + TabH)
            return pt.X < W / 3 ? Zone.Tab1 : pt.X < W * 2 / 3 ? Zone.Tab2 : Zone.Tab3;

        var mainRect = new Rectangle(_alerting ? (W - 150) / 2 : (W - 128) / 2, 232, _alerting ? 150 : 128, 38);
        if (mainRect.Contains(pt)) return Zone.Main;
        if (!_alerting && new Rectangle(mainRect.X - 76, 232, 64, 38).Contains(pt)) return Zone.Reset;

        if (mode == TimerMode.CountDown)
        {
            for (var i = 0; i < 3; i++)
                if (RPresets[i].Contains(pt)) return i == 0 ? Zone.P1 : i == 1 ? Zone.P2 : Zone.P3;
        }
        else if (mode == TimerMode.TargetTime)
        {
            if (RToday.Contains(pt)) return Zone.Today;
            if (RTomorrow.Contains(pt)) return Zone.Tomorrow;
            if (RPick.Contains(pt)) return Zone.Pick;
            if (RDaily.Contains(pt)) return Zone.Daily;
        }
        return Zone.None;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var z = HitTest(e.Location);
        if (z != _hover)
        {
            _hover = z;
            Cursor = z == Zone.None ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != Zone.None)
        {
            _hover = Zone.None;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var z = HitTest(e.Location);
        if (z == Zone.None && e.Y < TitleBarH)
        {
            // 标题区拖动窗口
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        switch (HitTest(e.Location))
        {
            case Zone.Close:
            case Zone.Minimize:
                HideToTray();
                break;
            case Zone.Tab1:
                SetMode(TimerMode.CountUp);
                break;
            case Zone.Tab2:
                SetMode(TimerMode.CountDown);
                break;
            case Zone.Tab3:
                SetMode(TimerMode.TargetTime);
                break;
            case Zone.Main:
                if (_alerting) StopAlert();
                else if (_engine.State == RunState.Running) _engine.Pause();
                else _engine.Start();
                Invalidate();
                break;
            case Zone.Reset:
                StopAlert();
                _engine.Reset();
                Invalidate();
                break;
            case Zone.P1:
                AddPreset(5);
                break;
            case Zone.P2:
                AddPreset(15);
                break;
            case Zone.P3:
                AddPreset(30);
                break;
            case Zone.Today:
                SetTargetDate(DateTime.Today, false);
                break;
            case Zone.Tomorrow:
                SetTargetDate(DateTime.Today.AddDays(1), false);
                break;
            case Zone.Pick:
                PickCustomDate();
                break;
            case Zone.Daily:
                var next = !_engine.DailyRepeat;
                _settings.DailyRepeat = next;
                _engine.SetTarget(_engine.Target, next);
                Invalidate();
                break;
        }
    }

    private void AddPreset(int minutes)
    {
        var current = _engine.Duration;
        var next = current + TimeSpan.FromMinutes(minutes);
        if (next.TotalSeconds > 99 * 3600 + 59 * 60 + 59) next = TimeSpan.FromSeconds(99 * 3600 + 59 * 60 + 59);
        _engine.SetCountdown(next);
        Invalidate();
    }

    private void SetTargetDate(DateTime date, bool custom)
    {
        _targetDate = date;
        _targetDateIsCustom = custom;
        _engine.SetTarget(date.Date.AddHours(_engine.Target.Hour).AddMinutes(_engine.Target.Minute), _engine.DailyRepeat);
        Invalidate();
    }

    private void PickCustomDate()
    {
        using var picker = new Form
        {
            Text = "选择日期",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(240, 96),
            ShowInTaskbar = false,
        };
        var dtp = new DateTimePicker
        {
            Format = DateTimePickerFormat.Long,
            Value = _targetDate,
            Location = new Point(16, 16),
            Width = 208,
        };
        var ok = new Button { Text = "确定", DialogResult = DialogResult.OK, Location = new Point(56, 52), Size = new Size(84, 28) };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Location = new Point(148, 52), Size = new Size(76, 28) };
        picker.Controls.Add(dtp);
        picker.Controls.Add(ok);
        picker.Controls.Add(cancel);
        picker.AcceptButton = ok;
        picker.CancelButton = cancel;
        if (picker.ShowDialog(this) == DialogResult.OK && dtp.Value.Date >= DateTime.Today)
            SetTargetDate(dtp.Value.Date, true);
    }

    // ---------- 系统行为 ----------

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 点 × 只是藏进托盘，退出请用托盘右键菜单。
        e.Cancel = true;
        HideToTray();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW; // 系统纸片阴影，零额外绘制成本
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.Ticked -= OnEngineTick;
            _engine.StateChanged -= OnEngineStateChanged;
            _alertTimer.Dispose();
            foreach (var f in new[] { _fTitle, _fTab, _fBig, _fBigAlert, _fSmall, _fBtn }) f.Dispose();
            foreach (var tb in new[] { _txtH, _txtM, _txtS, _txtTH, _txtTM }) tb.Font.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- 工具 ----------

    private static Rectangle SettingsBounds() => new(0, SettingsY, W, SettingsH);

    private static GraphicsPath RoundedRectPath(Rectangle r, int radius)
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

    private static float TextWidth(Graphics g, string text, Font f) => g.MeasureString(text, f).Width;

    private static Color Blend(Color a, Color b, double t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t),
        (int)(a.G + (b.G - a.G) * t),
        (int)(a.B + (b.B - a.B) * t));

    private const int CS_DROPSHADOW = 0x00020000;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const uint FLASHW_STOP = 0, FLASHW_ALL = 3, FLASHW_TIMER = 12;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }
}

internal static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, float radius)
    {
        var d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }
}
