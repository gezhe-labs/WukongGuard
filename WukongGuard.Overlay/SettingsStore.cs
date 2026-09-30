using System.Text.Json.Nodes;

namespace WukongGuard.Overlay;

internal static class SettingsStore
{
    internal static string? SmokeDirectory { get; set; }
    private static string SettingsPath => Path.Combine(SmokeDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WukongGuard"), "settings.json");

    internal static bool LoadHiddenAreaHints() => ReadBool("HiddenAreaHints", false);
    internal static bool LoadGamepadMenuHold() => ReadBool("GamepadMenuHold", true);

    internal static string LoadMoreHotkey()
    {
        try
        {
            var value = ReadSettings()["MoreHotkey"]?.GetValue<string>();
            return value == "CtrlAltShiftG" ? value : "CtrlShiftG";
        }
        catch { return "CtrlShiftG"; }
    }

    internal static void SaveHiddenAreaHints(bool enabled) => Save("HiddenAreaHints", enabled);
    internal static void SaveGamepadMenuHold(bool enabled) => Save("GamepadMenuHold", enabled);

    internal static void SaveMoreHotkey(string hotkey)
    {
        if (hotkey is not ("CtrlShiftG" or "CtrlAltShiftG"))
            throw new ArgumentOutOfRangeException(nameof(hotkey));
        Save("MoreHotkey", hotkey);
    }

    private static bool ReadBool(string key, bool fallback)
    {
        try { return ReadSettings()[key]?.GetValue<bool>() ?? fallback; }
        catch { return fallback; }
    }

    private static JsonObject ReadSettings()
    {
        if (!File.Exists(SettingsPath)) return new JsonObject();
        return JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject();
    }

    private static void Save(string key, bool value)
    {
        var settings = ReadSettings();
        settings[key] = value;
        SaveSettings(settings);
    }

    private static void Save(string key, string value)
    {
        var settings = ReadSettings();
        settings[key] = value;
        SaveSettings(settings);
    }

    private static void SaveSettings(JsonObject settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, settings.ToJsonString());
        File.Move(temporary, SettingsPath, true);
    }
}
