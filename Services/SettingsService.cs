using System.IO;
using System.Text.Json;

namespace DesktopIconMirror.Services;

public class AppSettings
{
    public string? TargetMonitorDeviceName { get; set; }
    public int IconSize { get; set; } = 48;
    public double BackgroundOpacity { get; set; } = 0.0;
    public int PositionPollIntervalMs { get; set; } = 1500;
    public bool AutoStart { get; set; }
    public bool MirrorLayout { get; set; } = true;
}

public static class SettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DeskMirror");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Ignore corrupt settings
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    public static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue("DeskMirror", $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue("DeskMirror", false);
            }
        }
        catch
        {
            // Ignore registry errors
        }
    }
}
