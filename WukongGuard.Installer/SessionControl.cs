using System.Diagnostics;

namespace WukongGuard.Installer;

internal static class SessionControl
{
    private const string GameProcess = "b1-Win64-Shipping";

    internal static bool IsGameRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(GameProcess);
            try { return processes.Length != 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    private static string ModDirectory(string gameRoot) => Path.Combine(gameRoot,
        "b1", "Binaries", "Win64", "CSharpLoader", "Mods", "WukongGuard");

    private static string SessionFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WukongGuard", "active-session.txt");

    internal static bool IsInstalled(string gameRoot) =>
        File.Exists(Path.Combine(ModDirectory(gameRoot), "WukongGuard.mod-disabled"));

    internal static bool IsActive(string gameRoot) =>
        File.Exists(Path.Combine(ModDirectory(gameRoot), "WukongGuard.dll"));

    internal static void Enable(string gameRoot)
    {
        if (IsGameRunning) throw new InvalidOperationException("请先完全退出游戏，再启用本次游戏。");
        var directory = ModDirectory(gameRoot);
        var source = Path.Combine(directory, "WukongGuard.mod-disabled");
        if (!File.Exists(source)) throw new InvalidOperationException("请先点击“安装 / 更新”。");
        Directory.CreateDirectory(Path.GetDirectoryName(SessionFile)!);
        File.WriteAllText(SessionFile, "armed");
        try { File.Copy(source, Path.Combine(directory, "WukongGuard.dll"), true); }
        catch { File.Delete(SessionFile); throw; }
        Installation.Log("One game session armed.");
    }

    internal static void Heartbeat()
    {
        if (File.Exists(SessionFile)) File.SetLastWriteTimeUtc(SessionFile, DateTime.UtcNow);
    }

    internal static void Disable(string gameRoot)
    {
        if (IsGameRunning) throw new InvalidOperationException("请先完全退出游戏，再停用插件。");
        File.Delete(Path.Combine(ModDirectory(gameRoot), "WukongGuard.dll"));
        File.Delete(SessionFile);
        Installation.Log("One game session disarmed.");
    }
}
