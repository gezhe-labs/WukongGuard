using System.Text.Json;

namespace WukongGuard.Overlay;

internal static class SettingsStore
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WukongGuard", "settings.json");

    internal static bool LoadHiddenAreaHints()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return false;
            using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
            return document.RootElement.GetProperty("HiddenAreaHints").GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    internal static void SaveHiddenAreaHints(bool enabled)
    {
        string directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { HiddenAreaHints = enabled }));
        File.Move(temporary, SettingsPath, true);
    }
}
