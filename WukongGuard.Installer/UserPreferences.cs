using System.Text.Json.Nodes;

namespace WukongGuard.Installer;

internal static class UserPreferences
{
    // Keep the original location and keys, so the rebrand retains existing choices.
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WukongGuard", "settings.json");

    internal static bool LoadHiddenAreaHints() => ReadBool("HiddenAreaHints", false);
    internal static bool LoadGamepadMenuHold() => ReadBool("GamepadMenuHold", true);
    internal static string LoadMoreHotkey()
    {
        try { return Read()["MoreHotkey"]?.GetValue<string>() == "CtrlAltShiftG"
            ? "CtrlAltShiftG" : "CtrlShiftG"; }
        catch { return "CtrlShiftG"; }
    }

    internal static void SaveHiddenAreaHints(bool value) => Save("HiddenAreaHints", value);
    internal static void SaveGamepadMenuHold(bool value) => Save("GamepadMenuHold", value);
    internal static void SaveMoreHotkey(string value)
    {
        if (value is not ("CtrlShiftG" or "CtrlAltShiftG"))
            throw new ArgumentOutOfRangeException(nameof(value));
        Save("MoreHotkey", value);
    }

    private static bool ReadBool(string name, bool fallback)
    {
        try { return Read()[name]?.GetValue<bool>() ?? fallback; }
        catch { return fallback; }
    }

    private static JsonObject Read() => File.Exists(SettingsPath)
        ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? new JsonObject()
        : new JsonObject();

    private static void Save(string name, object value)
    {
        var settings = Read();
        if (value is bool flag) settings[name] = flag;
        else settings[name] = (string)value;
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, settings.ToJsonString());
        File.Move(temporary, SettingsPath, true);
    }
}
