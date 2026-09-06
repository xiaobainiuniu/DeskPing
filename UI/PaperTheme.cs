using System.Drawing;

namespace DeskPing.UI;

/// <summary>画风色板（浅/深两版）。</summary>
public sealed record Palette(
    Color Paper,       // 纸面背景
    Color PaperBorder, // 纸面描边
    Color Text,        // 正文
    Color WeakText,    // 弱化文字（次要信息）
    Color Active,      // 强调（选中、主按钮）
    Color Code,        // 内嵌底色
    Color Link,        // 链接
    Color Danger,      // 警示（到点提醒）
    Color Tint);       // 暖色叠加基（hover 底纹）

/// <summary>
/// 纸张质感主题：暖纸 / 墨 / 林 / 霞 四套配色，支持深色模式。
/// </summary>
public static class PaperTheme
{
    public const string Warm = "warm";
    public const string Ink = "ink";
    public const string Forest = "forest";
    public const string Rose = "rose";

    public static readonly string[] All = { Warm, Ink, Forest, Rose };
    public static readonly string[] Names = { "暖纸", "墨", "林", "霞" };

    // 字体与形状常量（逻辑像素，随 DPI 自动缩放）。
    public const string FontName = "Microsoft YaHei UI";
    public const int CornerRadius = 12;     // 纸片外圆角
    public const int ControlRadius = 8;     // 按钮/输入框圆角
    public const int BorderWidth = 1;       // 纸片描边

    private static readonly Dictionary<string, (Palette Light, Palette Dark)> Schemes = new()
    {
        [Warm] = (
            new Palette(
                Color.FromArgb(255, 249, 234), Color.FromArgb(224, 206, 167), Color.FromArgb(51, 41, 30),
                Color.FromArgb(138, 122, 99), Color.FromArgb(140, 115, 80), Color.FromArgb(247, 237, 210),
                Color.FromArgb(176, 98, 66), Color.FromArgb(176, 90, 70), Color.FromArgb(120, 92, 48)),
            new Palette(
                Color.FromArgb(33, 31, 28), Color.FromArgb(76, 69, 61), Color.FromArgb(231, 224, 212),
                Color.FromArgb(146, 137, 123), Color.FromArgb(168, 142, 106), Color.FromArgb(45, 42, 38),
                Color.FromArgb(214, 150, 120), Color.FromArgb(230, 110, 90), Color.FromArgb(230, 223, 211))),

        [Ink] = (
            new Palette(
                Color.FromArgb(246, 247, 249), Color.FromArgb(208, 214, 222), Color.FromArgb(38, 44, 54),
                Color.FromArgb(118, 126, 138), Color.FromArgb(90, 108, 134), Color.FromArgb(236, 239, 243),
                Color.FromArgb(66, 104, 156), Color.FromArgb(188, 84, 80), Color.FromArgb(70, 90, 120)),
            new Palette(
                Color.FromArgb(26, 28, 32), Color.FromArgb(60, 66, 76), Color.FromArgb(222, 227, 234),
                Color.FromArgb(138, 146, 158), Color.FromArgb(132, 156, 188), Color.FromArgb(38, 41, 47),
                Color.FromArgb(132, 170, 214), Color.FromArgb(224, 116, 108), Color.FromArgb(180, 200, 228))),

        [Forest] = (
            new Palette(
                Color.FromArgb(243, 248, 241), Color.FromArgb(200, 218, 198), Color.FromArgb(38, 50, 42),
                Color.FromArgb(110, 128, 112), Color.FromArgb(88, 130, 96), Color.FromArgb(233, 242, 231),
                Color.FromArgb(60, 130, 96), Color.FromArgb(188, 96, 76), Color.FromArgb(70, 110, 80)),
            new Palette(
                Color.FromArgb(26, 30, 27), Color.FromArgb(58, 70, 60), Color.FromArgb(220, 228, 220),
                Color.FromArgb(134, 148, 136), Color.FromArgb(124, 168, 134), Color.FromArgb(37, 42, 38),
                Color.FromArgb(128, 190, 150), Color.FromArgb(222, 124, 104), Color.FromArgb(180, 208, 186))),

        [Rose] = (
            new Palette(
                Color.FromArgb(253, 245, 246), Color.FromArgb(228, 205, 210), Color.FromArgb(54, 38, 42),
                Color.FromArgb(140, 114, 120), Color.FromArgb(158, 104, 118), Color.FromArgb(248, 236, 238),
                Color.FromArgb(178, 84, 110), Color.FromArgb(188, 82, 78), Color.FromArgb(150, 80, 96)),
            new Palette(
                Color.FromArgb(33, 28, 30), Color.FromArgb(78, 64, 68), Color.FromArgb(232, 220, 223),
                Color.FromArgb(152, 132, 137), Color.FromArgb(190, 134, 148), Color.FromArgb(44, 38, 40),
                Color.FromArgb(224, 148, 170), Color.FromArgb(230, 114, 100), Color.FromArgb(224, 180, 190))),
    };

    public static Palette Current { get; private set; } = Schemes[Warm].Light;
    public static bool IsDark { get; private set; }

    public static void Apply(string schemeId, bool dark)
    {
        IsDark = dark;
        Current = Schemes.TryGetValue(schemeId, out var pair)
            ? (dark ? pair.Dark : pair.Light)
            : (dark ? Schemes[Warm].Dark : Schemes[Warm].Light);
    }

    public static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>与背景混合出 hover 底纹色（暖色叠加基）。</summary>
    public static Color HoverTint() => WithAlpha(Current.Tint, IsDark ? (byte)48 : (byte)32);

    /// <summary>把颜色向黑 / 白方向微调。</summary>
    public static Color Darken(Color c, double t)
    {
        var (r, g, b) = (c.R, c.G, c.B);
        return Color.FromArgb(c.A, (int)(r * (1 - t)), (int)(g * (1 - t)), (int)(b * (1 - t)));
    }
}
