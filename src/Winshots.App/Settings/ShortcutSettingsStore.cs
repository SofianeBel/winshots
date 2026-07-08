using System.Text.Json;

namespace Winshots.App.Settings;

public sealed class ShortcutSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public ShortcutSettingsStore(string? settingsPath = null)
    {
        SettingsPath = string.IsNullOrWhiteSpace(settingsPath) ? DefaultSettingsPath : settingsPath;
    }

    public string SettingsPath { get; }

    public static string DefaultSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Winshots",
        "settings.json");

    public ShortcutSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new ShortcutSettings();
        }

        try
        {
            ShortcutSettings? settings = JsonSerializer.Deserialize<ShortcutSettings>(File.ReadAllText(SettingsPath));
            return settings ?? new ShortcutSettings();
        }
        catch
        {
            return new ShortcutSettings();
        }
    }

    public void Save(ShortcutSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath) ?? ".");
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
