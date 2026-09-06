using System.Text.Json;

namespace DeskPing.Core;

/// <summary>用户设置（JSON 持久化到 %APPDATA%\DeskPing\settings.json）。</summary>
public sealed class AppSettings
{
    public string ThemeId { get; set; } = "warm";
    public bool Dark { get; set; }
    public string Lang { get; set; } = "zh";
    public string Mode { get; set; } = "countdown";
    public bool SoundOn { get; set; } = true;
    public bool AutoStart { get; set; }
    public bool TopMost { get; set; }
    public int LayoutV { get; set; }
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowW { get; set; } = -1;
    public int WindowH { get; set; } = -1;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskPing", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch
        {
            // 设置文件损坏时回退默认值，绝不让应用起不来。
        }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 保存失败不影响运行。
        }
    }
}
