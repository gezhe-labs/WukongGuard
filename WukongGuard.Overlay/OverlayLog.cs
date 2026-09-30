namespace WukongGuard.Overlay;

internal static class OverlayLog
{
    internal static string? SmokeDirectory { get; set; }

    internal static void Write(string message)
    {
        try
        {
            string folder = SmokeDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WukongGuard");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "overlay.log");
            if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception) { /* Diagnostics must not interfere with input or reminders. */ }
    }
}
