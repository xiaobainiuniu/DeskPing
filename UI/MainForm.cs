using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Media;
using System.Runtime.InteropServices;
using DeskPing.Core;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace DeskPing.UI;

/// <summary>
/// 主窗口：全自绘、无边框圆角纸片 + 系统阴影。
/// 紧凑布局：模式切换收进标题栏下拉，窗口默认约 308×280。
/// 支持拖动、边角缩放、置顶；所有绘制按需进行，空闲时 CPU 为零。
/// </summary>
public sealed class MainForm : Form
{
    private const int TitleBarH = 36;
    private const int StatusH = 20;
    private const int SettingsRowH = 40;
    private const int ButtonRowH = 38;
    private const int Edge = 14;
    private const int InputW = 32, InputH = 24, Gap = 6;

    private readonly TimerEngine _engine;
    private readonly AppSettings _settings;
    private readonly Action _onFirstHide;

    private readonly TextBox _txtH, _txtM, _txtS, _txtTH, _txtTM;
    private readonly TextBox[] _inputs;
    private readonly WinFormsTimer _alertTimer;
    private readonly WinFormsTimer _flashTimer;

    private DateTime _targetDate = DateTime.Today;
    private bool _targetDateIsCustom;
    private bool _alerting, _pinned;
    private double _animPhase;
    private int _soundCountdown;
    private bool _flashActive, _everHidden;
    private string _statusOverride = "";
    private Zone _hover = Zone.None, _pressed = Zone.None;
    private bool _syncingInputs;
    private bool _clickWasFocused;
    private int _fontFitW, _fontFitH;

    private Font _fTitle = null!, _fBig = null!, _fBigAlert = null!, _fSmall = null!, _fBtn = null!;
    private Icon? _formIcon;
    private IntPtr _formIconHandle;

    private ModePopup? _modePopup;
    private bool _suppressPopupReopen;

    public event Action? ThemeChanged;
    public bool Pinned => _pinned;

    public MainForm(TimerEngine engine, AppSettings settings, Action onFirstHide)
    {
        _engine = engine;
        _settings = settings;
        _onFirstHide = onFirstHide;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(300, 252);
        DoubleBuffered = true;
        ShowInTaskbar = true;

        // 先创建输入框，再设置窗口尺寸（否则 OnResize 里访问输入框会空引用）
        _txtH = MakeInput();
        _txtM = MakeInput();
        _txtS = MakeInput();
        _txtTH = MakeInput();
        _txtTM = MakeInput();
        _inputs = new[] { _txtH, _txtM, _txtS, _txtTH, _txtTM };
        MakeFonts();
        RebuildFonts();
        SetupInputs();

        // 尺寸：布局版本 >= 2 才恢复上次尺寸（旧版存的尺寸偏大，一次性放弃）
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 308, 280);
        var restore = settings.LayoutV >= 2 && settings.WindowW > 0 && settings.WindowH > 0;
        var w = Math.Min(restore ? settings.WindowW : 308, wa.Width - 24);
        var h = Math.Min(restore ? settings.WindowH : 280, wa.Height - 24);
        ClientSize = new Size(Math.Max(w, MinimumSize.Width), Math.Max(h, MinimumSize.Height));

        // 位置：恢复上次，否则屏幕右下角
        var x = settings.WindowX >= 0 ? settings.WindowX : wa.Right - Width - 48;
        var y = settings.WindowY >= 0 ? settings.WindowY : wa.Bottom - Height - 64;
        Location = new Point(Math.Clamp(x, wa.Left - Width + 80, wa.Right - 80), Math.Clamp(y, wa.Top, wa.Bottom - 60));

        _engine.Ticked += OnEngineTick;
        _engine.StateChanged += OnEngineStateChanged;

        ApplySettingsFromStore();
        UpdateModeUi();
        ApplyTheme();
        SetPinned(settings.TopMost);

        _alertTimer = new WinFormsTimer { Interval = 40 };
        _alertTimer.Tick += OnAlertTick;
        _flashTimer = new WinFormsTimer { Interval = 1200 };
        _flashTimer.Tick += OnFlashTick;
    }

    private static TextBox MakeInput()
    {
        return new TextBox
        {
            BorderStyle = BorderStyle.None,
            // 必须左对齐：居中文字会让光标渲染错位（跳到框最前面）
            TextAlign = HorizontalAlignment.Left,
            MaxLength = 2,
            Font = new Font(PaperTheme.FontName, 9.5f),
            TabStop = true,
            Visible = false,
        };
    }

    private void MakeFonts()
    {
        _fTitle = new Font(PaperTheme.FontName, 9.5f);
        _fSmall = new Font(PaperTheme.FontName, 8.5f);
        _fBtn = new Font(PaperTheme.FontName, 10f, FontStyle.Bold);
    }

    /// <summary>大数字随窗口大小缩放（拖动缩放窗口时保持比例协调）。</summary>
    private void RebuildFonts()
    {
        var scale = Math.Clamp(Math.Min(W / 308.0, H / 280.0), 0.62, 2.4);
        _fBig?.Dispose();
        _fBigAlert?.Dispose();
        _fBig = new Font(PaperTheme.FontName, (float)(38 * scale));
        _fBigAlert = new Font(PaperTheme.FontName, (float)(28 * scale), FontStyle.Bold);

        // 输入框字号温和跟随窗口缩放（每框独立 Font 实例，替换即释放）
        var inputSize = Math.Clamp(scale, 0.8f, 1.6f);
        foreach (var tb in _inputs)
        {
            var old = tb.Font;
            tb.Font = new Font(PaperTheme.FontName, (float)(9.5 * inputSize));
            old.Dispose();
        }
        SyncInputMargins();

        _fontFitW = W;
        _fontFitH = H;
    }

    private void SetupInputs()
    {
        _txtH.TextChanged += (_, _) => ApplyCountdownInputs();
        _txtM.TextChanged += (_, _) => ApplyCountdownInputs();
        _txtS.TextChanged += (_, _) => ApplyCountdownInputs();
        _txtTH.TextChanged += (_, _) => ApplyTargetInputs();
        _txtTM.TextChanged += (_, _) => ApplyTargetInputs();

        foreach (var tb in _inputs)
        {
            tb.KeyPress += (_, e) => e.Handled = !char.IsDigit(e.KeyChar) && e.KeyChar != '\b';
            // 第一次点击：全选方便直接覆盖；再点击：光标落到末尾正常闪烁
            tb.MouseDown += (_, _) => _clickWasFocused = tb.Focused;
            tb.MouseUp += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                if (!_clickWasFocused) tb.SelectAll();
                else
                {
                    tb.SelectionStart = tb.Text.Length;
                    tb.SelectionLength = 0;
                }
            };
            // 每敲一个数字，光标都停在最后一位后面（可继续输入或删除）
            tb.TextChanged += (_, _) =>
            {
                if (tb.Focused && !_syncingInputs)
                {
                    tb.SelectionStart = tb.Text.Length;
                    tb.SelectionLength = 0;
                }
            };
            tb.Enter += (_, _) =>
            {
                tb.SelectAll();
                Invalidate();
            };
            // 离开时把输入规整回两位数字（如 3 → 03）
            tb.Leave += (_, _) =>
            {
                UpdateInputsFromEngine();
                Invalidate();
            };
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
        CloseModePopup();
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

    public void SetPinned(bool pinned)
    {
        _pinned = pinned;
        if (!_alerting) TopMost = pinned;
        Invalidate();
    }

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
        s.WindowW = W;
        s.WindowH = H;
    }

    public void TriggerAlert()
    {
        if (_alerting) return;
        _alerting = true;
        _animPhase = 0;
        _soundCountdown = 0;
        CloseModePopup();

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
        TopMost = _pinned; // 恢复用户自己的置顶设置
        _engine.Reset();
        Invalidate();
    }

    private void HideToTray()
    {
        CloseModePopup();
        Hide();
        if (!_everHidden)
        {
            _everHidden = true;
            _onFirstHide();
        }
    }

    // ---------- 模式下拉 ----------

    private void OpenModePopup()
    {
        _modePopup?.Close();
        _modePopup = new ModePopup(_engine.Mode, PointToScreen(new Point(ModeBtnRect.Left, ModeBtnRect.Bottom + 3)),
            mode => SetMode(mode));
        _modePopup.FormClosed += (_, _) => _modePopup = null;
        _modePopup.Show();
        Invalidate();
    }

    private void CloseModePopup()
    {
        var popup = _modePopup;
        _modePopup = null;
        if (popup != null && !popup.IsDisposed) popup.Close();
    }

    // ---------- 引擎事件 ----------

    private void OnEngineTick()
    {
        if (Visible) Invalidate(TimeArea); // 窗口隐藏时零刷新
    }

    private void OnEngineStateChanged()
    {
        _statusOverride = "";
        UpdateInputsFromEngine();
        if (Visible) Invalidate();
    }

    // ---------- 提醒动画 ----------

    private void OnAlertTick(object? sender, EventArgs e)
    {
        _animPhase += 0.16;
        Invalidate(); // 边框呼吸 + 数字跳动，仅提醒期间全量重绘

        _soundCountdown++;
        if (_engine.SoundOn && _soundCountdown % 100 == 0) // 每 4 秒一声
            SystemSounds.Exclamation.Play();
    }

    private void OnFlashTick(object? sender, EventArgs e)
    {
        _flashTimer.Stop();
        _statusOverride = "";
        Invalidate();
    }

    private void FlashStatus(string text)
    {
        _statusOverride = text;
        _flashTimer.Stop();
        _flashTimer.Start();
        Invalidate();
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
            SetInputText(_txtH, d.Hours.ToString("D2"));
            SetInputText(_txtM, d.Minutes.ToString("D2"));
            SetInputText(_txtS, d.Seconds.ToString("D2"));

            var t = _engine.Target;
            SetInputText(_txtTH, t.Hour.ToString("D2"));
            SetInputText(_txtTM, t.Minute.ToString("D2"));

            _targetDate = t.Date;
            _targetDateIsCustom = t.Date != DateTime.Today && t.Date != DateTime.Today.AddDays(1);
        }
        finally
        {
            _syncingInputs = false;
        }
    }

    /// <summary>正在编辑的输入框不打断（避免打字时光标被拽走）；其余框规整为两位。</summary>
    private static void SetInputText(TextBox tb, string text)
    {
        if (tb.Focused) return;
        if (tb.Text != text) tb.Text = text;
        if (tb.SelectionStart != tb.Text.Length) tb.SelectionStart = tb.Text.Length;
    }

    private void UpdateModeUi()
    {
        var mode = _engine.Mode;
        _txtH.Visible = _txtM.Visible = _txtS.Visible = mode == TimerMode.CountDown;
        _txtTH.Visible = _txtTM.Visible = mode == TimerMode.TargetTime;
        LayoutInputs();
        UpdateInputsFromEngine();
        Invalidate();
    }

    // ---------- 动态布局 ----------

    private int W => ClientSize.Width;
    private int H => ClientSize.Height;
    private int SettingsTop => H - StatusH - ButtonRowH - 8 - SettingsRowH;
    private int RowY => SettingsTop + (SettingsRowH - InputH) / 2 + 1;
    private int ButtonsTop => H - StatusH - ButtonRowH - 4;
    private int SettingsSX => (W - 256) / 2;

    private Rectangle TimeArea => new(Edge, TitleBarH + 6, W - Edge * 2, SettingsTop - 14 - (TitleBarH + 6));
    private Rectangle StatusRect => new(Edge, H - StatusH, W - Edge * 2, StatusH);
    private Rectangle CloseRect => new(W - 40, 7, 28, 22);
    private Rectangle MinRect => new(W - 74, 7, 28, 22);
    private Rectangle PinRect => new(W - 108, 7, 28, 22);
    private Rectangle ModeBtnRect => new(92, 6, 96, 24);
    private Rectangle MainBtnRect => new(_alerting ? (W - 150) / 2 : (W - 194) / 2 + 74, ButtonsTop, _alerting ? 150 : 120, 38);
    private Rectangle ResetBtnRect => new((W - 194) / 2, ButtonsTop, 64, 38);

    private Rectangle RCountH => new(SettingsSX, RowY, InputW, InputH);
    private Rectangle RCountM => new(SettingsSX + 40, RowY, InputW, InputH);
    private Rectangle RCountS => new(SettingsSX + 80, RowY, InputW, InputH);
    private Rectangle RTargetH => new(SettingsSX, RowY, InputW, InputH);
    private Rectangle RTargetM => new(SettingsSX + 40, RowY, InputW, InputH);
    private Rectangle RPreset(int i) => new(SettingsSX + 118 + i * (42 + Gap), RowY, 42, InputH);
    private Rectangle RToday => new(SettingsSX + 78, RowY, 40, InputH);
    private Rectangle RTomorrow => new(SettingsSX + 124, RowY, 40, InputH);
    private Rectangle RPick => new(SettingsSX + 170, RowY, 32, InputH);
    private Rectangle RDaily => new(SettingsSX + 208, RowY, 48, InputH);

    private void LayoutInputs()
    {
        if (_engine.Mode == TimerMode.CountDown)
        {
            _txtH.Bounds = RCountH;
            _txtM.Bounds = RCountM;
            _txtS.Bounds = RCountS;
        }
        else if (_engine.Mode == TimerMode.TargetTime)
        {
            _txtTH.Bounds = RTargetH;
            _txtTM.Bounds = RTargetM;
        }
    }

    /// <summary>左对齐 + 左右等边距：数字在框内居中显示，光标渲染位置始终正确。</summary>
    private void SyncInputMargins()
    {
        foreach (var tb in _inputs) SetInputMargins(tb);
    }

    private static void SetInputMargins(TextBox tb)
    {
        var w = 0;
        using (var g = tb.CreateGraphics())
            w = (int)Math.Ceiling(g.MeasureString("88", tb.Font).Width);
        var margin = Math.Max(0, (InputW - w) / 2 - 1);
        SendMessage(tb.Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN),
            (IntPtr)((margin << 16) | (margin & 0xFFFF)));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_txtH == null) return; // 构造早期尚未创建输入框
        UpdateRoundedRegion();
        LayoutInputs();
        if (Math.Abs(W - _fontFitW) + Math.Abs(H - _fontFitH) > 12) RebuildFonts();
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // 系统缩放完字体后，重新同步输入框边距
        if (IsHandleCreated) BeginInvoke((Action)SyncInputMargins);
    }

    // ---------- 主题 ----------

    private void ApplyTheme()
    {
        var p = PaperTheme.Current;
        BackColor = p.Paper;
        ForeColor = p.Text;
        foreach (var tb in _inputs)
        {
            tb.BackColor = p.Paper;
            tb.ForeColor = p.Text;
        }
        ApplyFormIcon();
        UpdateRoundedRegion();
        Invalidate();
    }

    private void ApplyFormIcon()
    {
        using var bmp = TrayIcon.CreateClockBitmap();
        var newHandle = bmp.GetHicon();
        var newIcon = Icon.FromHandle(newHandle);
        var oldHandle = _formIconHandle;
        Icon = newIcon;
        _formIcon = newIcon;
        _formIconHandle = newHandle;
        if (oldHandle != IntPtr.Zero) DestroyIcon(oldHandle);
    }

    private void UpdateRoundedRegion()
    {
        var r = new Rectangle(0, 0, W, H);
        if (r.Width <= 0 || r.Height <= 0) return;
        var radius = Math.Min(PaperTheme.CornerRadius, Math.Min(r.Width, r.Height) / 2);
        using var path = RoundedRectPath(r, radius);
        Region?.Dispose();
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
        DrawTime(g, p);
        DrawSettings(g, p);
        DrawButtons(g, p);
        DrawStatus(g, p);
    }

    /// <summary>纸片描边 + 分隔线 + 右下角缩放手柄。提醒时描边呼吸转警示色。</summary>
    private void DrawChrome(Graphics g, Palette p)
    {
        var radius = Math.Min(PaperTheme.CornerRadius, Math.Min(W, H) / 2);
        if (_alerting)
        {
            var k = (Math.Sin(_animPhase) + 1) / 2;
            var border = Blend(p.PaperBorder, p.Danger, k);
            using var pen = new Pen(border, 2f);
            using var path = RoundedRectPath(new Rectangle(1, 1, W - 3, H - 3), radius);
            g.DrawPath(pen, path);
        }
        else
        {
            using var pen = new Pen(p.PaperBorder, PaperTheme.BorderWidth);
            using var path = RoundedRectPath(new Rectangle(1, 1, W - 3, H - 3), radius);
            g.DrawPath(pen, path);
        }

        using (var line = new Pen(PaperTheme.WithAlpha(p.PaperBorder, 110), 1f))
            g.DrawLine(line, 14, TitleBarH, W - 14, TitleBarH);

        // 右下角缩放手柄
        using (var grip = new Pen(PaperTheme.WithAlpha(p.WeakText, 110), 1f))
        {
            for (var i = 0; i < 3; i++)
                g.DrawLine(grip, W - 16 + i * 4, H - 5, W - 5, H - 16 + i * 4);
        }
    }

    private void DrawTitleBar(Graphics g, Palette p)
    {
        using (var dot = new SolidBrush(p.Active))
            g.FillEllipse(dot, 16, TitleBarH / 2f - 3, 6, 6);
        using (var brush = new SolidBrush(p.Text))
            g.DrawString("DeskPing", _fTitle, brush, 28, (TitleBarH - _fTitle.Height) / 2f + 1);

        DrawModeButton(g, p);
        DrawPinButton(g, p);
        DrawTitleButton(g, p, MinRect, "−", _hover == Zone.Minimize, _pressed == Zone.Minimize);
        DrawTitleButton(g, p, CloseRect, "✕", _hover == Zone.Close, _pressed == Zone.Close);
    }

    private void DrawModeButton(Graphics g, Palette p)
    {
        var r = ModeBtnRect;
        var open = _modePopup is { Visible: true };
        using (var path = RoundedRectPath(r, 7))
        {
            if (open)
            {
                using var b = new SolidBrush(p.Code);
                g.FillPath(b, path);
            }
            else if (_hover == Zone.Mode || _pressed == Zone.Mode)
            {
                using var b = new SolidBrush(_pressed == Zone.Mode ? PaperTheme.WithAlpha(p.Tint, 60) : PaperTheme.HoverTint());
                g.FillPath(b, path);
            }
        }
        using (var brush = new SolidBrush(p.Text))
        {
            var text = ModeName(_engine.Mode);
            var tw = TextWidth(g, text, _fSmall);
            g.DrawString(text, _fSmall, brush, r.X + 12, r.Y + (r.Height - _fSmall.Height) / 2f + 1);
            // ▾ 小三角
            using var tri = new Pen(p.WeakText, 1.4f);
            var tx = r.X + 12 + tw + 8;
            var ty = r.Y + r.Height / 2f;
            g.DrawLine(tri, tx, ty - 2, tx + 4, ty + 2);
            g.DrawLine(tri, tx + 4, ty + 2, tx + 8, ty - 2);
        }
    }

    private void DrawPinButton(Graphics g, Palette p)
    {
        var r = PinRect;
        using (var path = RoundedRectPath(r, 6))
        {
            if (_hover == Zone.Pin || _pinned)
            {
                using var b = new SolidBrush(_pinned ? p.Code : PaperTheme.HoverTint());
                g.FillPath(b, path);
            }
        }

        var cx = r.X + r.Width / 2f;
        var cy = r.Y + r.Height / 2f;
        using (var pen = new Pen(_pinned ? p.Active : p.WeakText, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(pen, cx - 3.5f, cy + 4.5f, cx + 4.5f, cy - 3.5f);
            g.DrawEllipse(pen, cx + 1.5f, cy - 7f, 5.5f, 5.5f);
        }
        if (_pinned)
        {
            using var b = new SolidBrush(p.Active);
            g.FillEllipse(b, cx + 2.5f, cy - 6f, 3.5f, 3.5f);
        }
    }

    private void DrawTitleButton(Graphics g, Palette p, Rectangle r, string glyph, bool hover, bool pressed)
    {
        using var path = RoundedRectPath(r, 6);
        if (hover || pressed)
        {
            using var b = new SolidBrush(pressed ? PaperTheme.WithAlpha(p.Tint, 60) : PaperTheme.HoverTint());
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

    private void DrawTime(Graphics g, Palette p)
    {
        // 纸张横线衬底（极淡，仅在非提醒状态）
        if (!_alerting)
        {
            using var rule = new Pen(PaperTheme.WithAlpha(p.PaperBorder, 40), 1f);
            for (var i = 1; i <= 3; i++)
            {
                var ruleY = TimeArea.Top + TimeArea.Height * i / 4f;
                g.DrawLine(rule, TimeArea.Left + 10, ruleY, TimeArea.Right - 10, ruleY);
            }
        }

        var text = DisplayText();
        var font = _alerting ? _fBigAlert : _fBig;
        var color = _alerting ? p.Danger : p.Text;
        var tw = TextWidth(g, text, font);
        var baseY = TimeArea.Top + (TimeArea.Height - font.Height) / 2f + 4;
        var y = baseY + (_alerting ? (float)(Math.Sin(_animPhase) * 6) : 0);

        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, (W - tw) / 2f, y);

        if (_engine.Mode == TimerMode.TargetTime && !_alerting && _engine.State == RunState.Running)
        {
            using var weak = new SolidBrush(p.WeakText);
            var hint = "剩余时间";
            var hw = TextWidth(g, hint, _fSmall);
            g.DrawString(hint, _fSmall, weak, (W - hw) / 2f, TimeArea.Bottom - _fSmall.Height - 6);
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
                    g.DrawString(hint, _fSmall, weak, (W - w) / 2f, SettingsTop + (SettingsRowH - _fSmall.Height) / 2f);
                }
                break;
        }
    }

    private void DrawCountdownSettings(Graphics g, Palette p)
    {
        using var weak = new SolidBrush(p.WeakText);
        g.DrawString(":", _fSmall, weak, SettingsSX + 32, RowY + (InputH - _fSmall.Height) / 2f - 2);
        g.DrawString(":", _fSmall, weak, SettingsSX + 72, RowY + (InputH - _fSmall.Height) / 2f - 2);

        DrawInputUnderline(g, p, _txtH);
        DrawInputUnderline(g, p, _txtM);
        DrawInputUnderline(g, p, _txtS);

        var presets = new[] { "+5分", "+15分", "+30分" };
        for (var i = 0; i < 3; i++)
        {
            var zone = i == 0 ? Zone.P1 : i == 1 ? Zone.P2 : Zone.P3;
            DrawChipButton(g, p, RPreset(i), presets[i], _hover == zone, _pressed == zone, false);
        }
    }

    private void DrawTargetSettings(Graphics g, Palette p)
    {
        using var weak = new SolidBrush(p.WeakText);
        g.DrawString(":", _fSmall, weak, SettingsSX + 32, RowY + (InputH - _fSmall.Height) / 2f - 2);

        DrawInputUnderline(g, p, _txtTH);
        DrawInputUnderline(g, p, _txtTM);

        var todaySel = !_targetDateIsCustom && _targetDate == DateTime.Today;
        var tomorrowSel = !_targetDateIsCustom && _targetDate == DateTime.Today.AddDays(1);
        DrawChipButton(g, p, RToday, "今天", _hover == Zone.Today, _pressed == Zone.Today, todaySel);
        DrawChipButton(g, p, RTomorrow, "明天", _hover == Zone.Tomorrow, _pressed == Zone.Tomorrow, tomorrowSel);
        DrawChipButton(g, p, RPick, "…", _hover == Zone.Pick, _pressed == Zone.Pick, _targetDateIsCustom);

        var checkRect = new Rectangle(RDaily.X + 2, RowY + 5, 14, 14);
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
            g.DrawString("每日", _fSmall, brush, RDaily.X + 18, RowY + (InputH - _fSmall.Height) / 2f + 2);
    }

    private void DrawInputUnderline(Graphics g, Palette p, TextBox tb)
    {
        using var pen = new Pen(tb.Focused ? p.Active : p.WeakText, tb.Focused ? 2f : 1f);
        g.DrawLine(pen, tb.Left - 4, tb.Bottom + 3, tb.Right + 4, tb.Bottom + 3);
    }

    private void DrawChipButton(Graphics g, Palette p, Rectangle r, string text, bool hover, bool pressed, bool selected)
    {
        using var path = RoundedRectPath(r, PaperTheme.ControlRadius - 2);
        if (selected)
        {
            using var b = new SolidBrush(p.Code);
            g.FillPath(b, path);
        }
        else if (pressed)
        {
            using var b = new SolidBrush(PaperTheme.WithAlpha(p.Tint, 60));
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
            var tw = TextWidth(g, text, _fSmall);
            g.DrawString(text, _fSmall, brush, r.X + (r.Width - tw) / 2f, r.Y + (r.Height - _fSmall.Height) / 2f + 1);
        }
    }

    private void DrawButtons(Graphics g, Palette p)
    {
        if (!_alerting)
            DrawOutlineButton(g, p, ResetBtnRect, "重置", _hover == Zone.Reset, _pressed == Zone.Reset);

        var mainText = _alerting ? "知道了" : _engine.State == RunState.Running ? "暂停" : "开始";
        var mainColor = _alerting ? p.Danger : p.Active;
        DrawFilledButton(g, p, MainBtnRect, mainText, _hover == Zone.Main, _pressed == Zone.Main, mainColor);
    }

    private void DrawFilledButton(Graphics g, Palette p, Rectangle r, string text, bool hover, bool pressed, Color fill)
    {
        var t = pressed ? 0.24 : hover ? 0.12 : 0.0;
        using var path = RoundedRectPath(r, 10);
        using (var b = new SolidBrush(PaperTheme.Darken(fill, t)))
            g.FillPath(b, path);
        using (var brush = new SolidBrush(p.Paper))
        {
            var tw = TextWidth(g, text, _fBtn);
            g.DrawString(text, _fBtn, brush, r.X + (r.Width - tw) / 2f, r.Y + (r.Height - _fBtn.Height) / 2f + 1);
        }
    }

    private void DrawOutlineButton(Graphics g, Palette p, Rectangle r, string text, bool hover, bool pressed)
    {
        using var path = RoundedRectPath(r, 10);
        if (pressed)
        {
            using var b = new SolidBrush(PaperTheme.WithAlpha(p.Tint, 60));
            g.FillPath(b, path);
        }
        else if (hover)
        {
            using var b = new SolidBrush(PaperTheme.HoverTint());
            g.FillPath(b, path);
        }
        using var pen = new Pen(PaperTheme.WithAlpha(p.WeakText, 180), 1f);
        g.DrawPath(pen, path);
        using (var brush = new SolidBrush(p.Text))
        {
            var tw = TextWidth(g, text, _fBtn);
            g.DrawString(text, _fBtn, brush, r.X + (r.Width - tw) / 2f, r.Y + (r.Height - _fBtn.Height) / 2f + 1);
        }
    }

    private void DrawStatus(Graphics g, Palette p)
    {
        using var brush = new SolidBrush(_alerting ? p.Danger : _statusOverride != "" ? p.Active : p.WeakText);
        var text = StatusText();
        var tw = TextWidth(g, text, _fSmall);
        g.DrawString(text, _fSmall, brush, (W - tw) / 2f, StatusRect.Y + (StatusRect.Height - _fSmall.Height) / 2f);
    }

    // ---------- 文案 ----------

    private static string ModeName(TimerMode m) => m switch
    {
        TimerMode.CountUp => "正计时",
        TimerMode.CountDown => "倒计时",
        _ => "目标时刻",
    };

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
        if (_statusOverride != "") return _statusOverride;
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

    private enum Zone { None, Close, Minimize, Pin, Mode, Main, Reset, P1, P2, P3, Today, Tomorrow, Pick, Daily }

    private Zone HitTest(Point pt)
    {
        if (CloseRect.Contains(pt)) return Zone.Close;
        if (MinRect.Contains(pt)) return Zone.Minimize;
        if (PinRect.Contains(pt)) return Zone.Pin;
        if (ModeBtnRect.Contains(pt)) return Zone.Mode;

        if (MainBtnRect.Contains(pt)) return Zone.Main;
        if (!_alerting && ResetBtnRect.Contains(pt)) return Zone.Reset;

        if (_engine.Mode == TimerMode.CountDown)
        {
            for (var i = 0; i < 3; i++)
                if (RPreset(i).Contains(pt)) return i == 0 ? Zone.P1 : i == 1 ? Zone.P2 : Zone.P3;
        }
        else if (_engine.Mode == TimerMode.TargetTime)
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
        if (_hover != Zone.None || _pressed != Zone.None)
        {
            _hover = Zone.None;
            _pressed = Zone.None;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var z = HitTest(e.Location);
        if (z == Zone.None)
        {
            if (e.Y < TitleBarH)
            {
                // 标题区拖动窗口
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            return;
        }
        if (z == Zone.Mode)
            _suppressPopupReopen = _modePopup is { Visible: true }; // 点按钮关闭弹层，别立刻重开
        _pressed = z;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _pressed = Zone.None;
        Invalidate();

        switch (HitTest(e.Location))
        {
            case Zone.Close:
            case Zone.Minimize:
                HideToTray();
                break;
            case Zone.Pin:
                SetPinned(!_pinned);
                break;
            case Zone.Mode:
                if (_suppressPopupReopen)
                {
                    _suppressPopupReopen = false;
                    return;
                }
                OpenModePopup();
                break;
            case Zone.Main:
                _statusOverride = "";
                if (_alerting) StopAlert();
                else if (_engine.State == RunState.Running) _engine.Pause();
                else _engine.Start();
                Invalidate();
                break;
            case Zone.Reset:
                StopAlert();
                _engine.Reset();
                FlashStatus("已重置");
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

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != WM_NCHITTEST) return;

        // 无边框窗口手动实现边角缩放
        var x = unchecked((short)(long)m.LParam);
        var y = unchecked((short)((long)m.LParam >> 16));
        var pt = PointToClient(new Point(x, y));
        var r = ClientRectangle;
        const int grab = 6;
        var left = pt.X <= grab;
        var right = pt.X >= r.Right - grab;
        var top = pt.Y <= grab;
        var bottom = pt.Y >= r.Bottom - grab;

        var ht = HTCLIENT;
        if (right && bottom) ht = HTBOTTOMRIGHT;
        else if (left && bottom) ht = HTBOTTOMLEFT;
        else if (right && top) ht = HTTOPRIGHT;
        else if (left && top) ht = HTTOPLEFT;
        else if (right) ht = HTRIGHT;
        else if (bottom) ht = HTBOTTOM;
        else if (left) ht = HTLEFT;
        else if (top) ht = HTTOP;
        if (ht != HTCLIENT) m.Result = (IntPtr)ht;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW; // 系统阴影，零额外绘制成本
            return cp;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _engine.Ticked -= OnEngineTick;
            _engine.StateChanged -= OnEngineStateChanged;
            CloseModePopup();
            _alertTimer.Dispose();
            _flashTimer.Dispose();
            foreach (var f in new[] { _fTitle, _fBig, _fBigAlert, _fSmall, _fBtn }) f.Dispose();
            foreach (var tb in _inputs) tb.Font.Dispose();
            if (_formIconHandle != IntPtr.Zero)
            {
                DestroyIcon(_formIconHandle);
                _formIconHandle = IntPtr.Zero;
            }
            _formIcon?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---------- 工具 ----------

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
    private const int EM_SETMARGINS = 0x00D3;
    private const int EC_LEFTMARGIN = 0x0001;
    private const int EC_RIGHTMARGIN = 0x0002;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12;
    private const int HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const uint FLASHW_STOP = 0, FLASHW_ALL = 3, FLASHW_TIMER = 12;

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    /// <summary>模式选择弹出层：纸张风小菜单，点外面 / 按 Esc / 选中后自动关闭。</summary>
    private sealed class ModePopup : Form
    {
        private static readonly (TimerMode Mode, string Name)[] Items =
        {
            (TimerMode.CountUp, "正计时"),
            (TimerMode.CountDown, "倒计时"),
            (TimerMode.TargetTime, "目标时刻"),
        };

        private readonly TimerMode _current;
        private readonly Action<TimerMode> _onPick;
        private int _hover = -1;

        public ModePopup(TimerMode current, Point anchorScreen, Action<TimerMode> onPick)
        {
            _current = current;
            _onPick = onPick;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(120, Items.Length * 32 + 12);
            DoubleBuffered = true;
            KeyPreview = true;
            Location = ClampToScreen(anchorScreen);
            UpdateRoundedRegion();
            Deactivate += (_, _) => Close();
        }

        private Point ClampToScreen(Point p)
        {
            var scr = Screen.FromPoint(p).WorkingArea;
            var x = Math.Clamp(p.X, scr.Left + 4, scr.Right - Width - 4);
            var y = p.Y;
            if (y + Height > scr.Bottom) y = p.Y - Height - 4; // 空间不够就向上弹
            y = Math.Clamp(y, scr.Top + 4, scr.Bottom - Height - 4);
            return new Point(x, y);
        }

        private void UpdateRoundedRegion()
        {
            using var path = new GraphicsPath();
            var d = 16;
            var r = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            Region = new Region(path);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var p = PaperTheme.Current;

            using (var bg = new SolidBrush(p.Paper))
                g.FillRectangle(bg, ClientRectangle);

            using (var pen = new Pen(p.PaperBorder, 1f))
            {
                var d = 16;
                var r = new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
                using var path = new GraphicsPath();
                path.AddArc(r.X, r.Y, d, d, 180, 90);
                path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                g.DrawPath(pen, path);
            }

            using var font = new Font(PaperTheme.FontName, 9.5f);
            for (var i = 0; i < Items.Length; i++)
            {
                var row = new Rectangle(6, 6 + i * 32, ClientSize.Width - 12, 32);
                var isCurrent = Items[i].Mode == _current;

                if (_hover == i)
                {
                    using var b = new SolidBrush(PaperTheme.HoverTint());
                    using var path = new GraphicsPath();
                    var d2 = 14;
                    path.AddArc(row.X, row.Y, d2, d2, 180, 90);
                    path.AddArc(row.Right - d2, row.Y, d2, d2, 270, 90);
                    path.AddArc(row.Right - d2, row.Bottom - d2, d2, d2, 0, 90);
                    path.AddArc(row.X, row.Bottom - d2, d2, d2, 90, 90);
                    path.CloseFigure();
                    g.FillPath(b, path);
                }

                using (var brush = new SolidBrush(isCurrent ? p.Text : p.WeakText))
                    g.DrawString(Items[i].Name, font, brush, row.X + 14, row.Y + (row.Height - font.Height) / 2f + 1);

                if (isCurrent)
                {
                    using var pen = new Pen(p.Active, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    var cx = row.Right - 20;
                    var cy = row.Y + row.Height / 2f;
                    g.DrawLine(pen, cx - 4, cy, cx - 1, cy + 3.5f);
                    g.DrawLine(pen, cx - 1, cy + 3.5f, cx + 4.5f, cy - 3.5f);
                }
            }
            font.Dispose();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var i = e.Y >= 6 && e.Y < 6 + Items.Length * 32 ? (e.Y - 6) / 32 : -1;
            if (e.X < 6 || e.X >= ClientSize.Width - 6) i = -1;
            if (i != _hover)
            {
                _hover = i;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1)
            {
                _hover = -1;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || _hover < 0) return;
            var picked = Items[_hover];
            _onPick(picked.Mode);
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }
    }
}
