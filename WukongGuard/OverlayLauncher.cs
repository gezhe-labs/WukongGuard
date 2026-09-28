using System;
using System.Diagnostics;
using System.IO;

namespace WukongGuard
{
    internal static class OverlayLauncher
    {
        internal static void EnsureRunning()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    "CSharpLoader", "Mods", "WukongGuard", "Overlay", "WukongGuard.Overlay.exe");
                if (!File.Exists(path))
                {
                    TraceLog.Write("[WukongGuard] packaged overlay not found; start it separately for development");
                    return;
                }
                var start = new ProcessStartInfo(path, "--exit-with-game")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(path)
                };
                var bundledRuntime = Path.Combine(Path.GetDirectoryName(path), "Runtime");
                if (File.Exists(Path.Combine(bundledRuntime, "dotnet.exe")))
                    start.EnvironmentVariables["DOTNET_ROOT"] = bundledRuntime;
                Process.Start(start);
                TraceLog.Write("[WukongGuard] packaged overlay launch requested");
            }
            catch (Exception ex)
            {
                TraceLog.Write("[WukongGuard] packaged overlay launch failed: " + ex);
            }
        }
    }
}
