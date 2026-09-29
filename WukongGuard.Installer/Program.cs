using System.Threading;

namespace WukongGuard.Installer;

internal static class Program
{
    internal const string OpenPanelEvent = @"Local\RegretPillOpenPanel";
    internal const string OpenSettingsEvent = @"Local\RegretPillOpenSettings";
    internal const string QuitEvent = @"Local\RegretPillQuit";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--verify")
        {
            try { Installation.VerifyPayload(); return 0; }
            catch (Exception ex) { Installation.Log(ex.ToString()); return 1; }
        }
        if ((args.Length == 3 || args.Length == 5) && args[0] == "--install"
            && args[1] == "--game-root" && (args.Length == 3 || args[3] == "--sid"))
        {
            try
            {
                Installation.InstallAsync(args[2], _ => { }, args.Length == 5 ? args[4] : null)
                    .GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex) { Installation.Log(ex.ToString()); return 1; }
        }
        if (args.Length == 3 && args[0] == "--disable" && args[1] == "--game-root")
        {
            try { SessionControl.Disable(args[2]); return 0; }
            catch (Exception ex) { Installation.Log(ex.ToString()); return 1; }
        }
        if (args.Length == 3 && args[0] == "--wait-clean" && args[1] == "--game-root")
        {
            try
            {
                while (SessionControl.IsGameRunning) Thread.Sleep(3000);
                for (var attempt = 0; attempt < 10; attempt++)
                {
                    try { SessionControl.Disable(args[2]); return 0; }
                    catch (IOException) { Thread.Sleep(3000); }
                }
                return 2;
            }
            catch (Exception ex) { Installation.Log(ex.ToString()); return 1; }
        }
        if (args.Length == 5 && args[0] == "--grant-session-access"
            && args[1] == "--game-root" && args[3] == "--sid")
        {
            try { Installation.RepairSessionAccess(args[2], args[4]); return 0; }
            catch (Exception ex) { Installation.Log(ex.ToString()); return 1; }
        }
        if (args.Length == 3 && args[0] == "--smoke-session" && args[1] == "--game-root")
        {
            try
            {
                SessionControl.SetSmokeSessionFile(args[2]);
                SessionControl.Enable(args[2]);
                if (!SessionControl.IsActive(args[2]) || !File.Exists(SessionControl.SessionFile))
                    return 2;
                SessionControl.Heartbeat();
                SessionControl.Disable(args[2]);
                return !SessionControl.IsActive(args[2]) && !File.Exists(SessionControl.SessionFile)
                    ? 0 : 3;
            }
            catch (Exception ex) { Installation.Log(ex.ToString()); return 1; }
        }

        using var singleInstance = new Mutex(true, @"Local\RegretPillLauncher", out var created);
        if (!created)
        {
            try { using var existing = EventWaitHandle.OpenExisting(OpenPanelEvent); existing.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            return 0;
        }
        using var openPanel = new EventWaitHandle(false, EventResetMode.AutoReset, OpenPanelEvent);
        using var openSettings = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEvent);
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEvent);
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm(openPanel, openSettings, quit));
        return 0;
    }
}
