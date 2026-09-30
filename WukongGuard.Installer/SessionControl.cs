using System.Diagnostics;

namespace WukongGuard.Installer;

internal static class SessionControl
{
    private const string GameProcess = "b1-Win64-Shipping";
    private static string? smokeSessionFile;

    internal static bool IsGameRunning
    {
        get
        {
            var processes = Process.GetProcessesByName(GameProcess);
            try { return processes.Length != 0; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    internal static string ModDirectory(string gameRoot) => Path.Combine(gameRoot,
        "b1", "Binaries", "Win64", "CSharpLoader", "Mods", "WukongGuard");

    internal static string SessionFile => smokeSessionFile ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WukongGuard", "active-session.txt");

    internal static void SetSmokeSessionFile(string gameRoot) =>
        smokeSessionFile = Path.Combine(gameRoot, ".smoke-session.txt");

    internal static bool IsInstalled(string gameRoot) =>
        File.Exists(Path.Combine(ModDirectory(gameRoot), "WukongGuard.mod-disabled"));

    internal static bool IsActive(string gameRoot) =>
        File.Exists(Path.Combine(ModDirectory(gameRoot), "WukongGuard.dll"));

    internal static bool CanWrite(string gameRoot)
    {
        var path = Path.Combine(ModDirectory(gameRoot), ".session-write-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(path, ""); File.Delete(path); return true; }
        catch (UnauthorizedAccessException) { return false; }
        catch (IOException) { return false; }
        finally { try { File.Delete(path); } catch { } }
    }

    internal static void Enable(string gameRoot)
    {
        WithMutation(() => EnableCore(gameRoot));
    }

    private static void EnableCore(string gameRoot)
    {
        if (IsGameRunning) throw new InvalidOperationException("请先完全退出游戏，再启用本次游戏。");
        var directory = ModDirectory(gameRoot);
        var source = Path.Combine(directory, "WukongGuard.mod-disabled");
        if (!File.Exists(source)) throw new InvalidOperationException("游戏组件尚未准备完成。");
        Directory.CreateDirectory(Path.GetDirectoryName(SessionFile)!);
        var statusFile = Path.Combine(Path.GetDirectoryName(SessionFile)!, "runtime-status.txt");
        try { File.Delete(statusFile); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
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
        WithMutation(() => DisableCore(gameRoot));
    }

    private static void DisableCore(string gameRoot)
    {
        // The in-game DLL can remain locked until the game exits. The lease is
        // revoked immediately; a later launch removes any stale DLL first.
        File.Delete(SessionFile);
        if (!IsGameRunning)
            File.Delete(Path.Combine(ModDirectory(gameRoot), "WukongGuard.dll"));
        Installation.Log("One game session disarmed.");
    }

    internal static void CleanupDisabled(string gameRoot)
    {
        WithMutation(() =>
        {
            // A cleanup helper from the previous session must not revoke a new one.
            if (!IsGameRunning && !File.Exists(SessionFile))
                File.Delete(Path.Combine(ModDirectory(gameRoot), "WukongGuard.dll"));
        });
    }

    private static void WithMutation(Action action)
    {
        using var mutex = new Mutex(false, @"Local\RegretPillSessionMutation");
        bool held;
        try { held = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { held = true; }
        if (!held) throw new IOException("游戏会话正在切换，请稍后重试。");
        try { action(); }
        finally { mutex.ReleaseMutex(); }
    }
}
