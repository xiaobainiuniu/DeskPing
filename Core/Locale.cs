namespace DeskPing.Core;

/// <summary>
/// 界面语言：默认中文，可切换英文。
/// 所有用户可见文案统一经 T(中文, English) 取值，运行时零成本切换。
/// </summary>
public static class Locale
{
    public static bool IsEnglish { get; private set; }

    public static void Set(bool english) => IsEnglish = english;

    /// <summary>按当前语言取文案。</summary>
    public static string T(string zh, string en) => IsEnglish ? en : zh;
}
