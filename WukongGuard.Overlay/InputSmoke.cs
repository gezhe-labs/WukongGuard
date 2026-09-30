using System.Runtime.InteropServices;

namespace WukongGuard.Overlay;

internal static class InputSmoke
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        SettingsStore.SmokeDirectory = Path.Combine(output, "profile");
        RegretPill.Shared.HistoryStore.SmokeDirectory = SettingsStore.SmokeDirectory;
        OverlayLog.SmokeDirectory = SettingsStore.SmokeDirectory;
        // No real controller, game, user preferences or saved games are changed by this test.
        SettingsStore.SaveGamepadMenuHold(false);
        var report = new List<string>();
        var previous = WindowFocus.GetForegroundWindow();
        using var alert = new AlertForm(managedSession: true);
        int exitCode = 1;
        alert.Shown += async (_, _) =>
        {
            try
            {
                CheckMenuGesture();
                await Task.Delay(100);
                var foreground = WindowFocus.GetForegroundWindow();
                report.Add($"Foreground before_start={previous}, after_start={foreground}, toast={alert.Handle}");
                Assert(foreground != IntPtr.Zero && foreground != alert.Handle,
                    "Starting the hidden overlay does not take foreground");
                if (foreground != previous) report.Add("NOTE: Another foreground change occurred during startup");
                alert.ShowAlert("overlay_demo_input", new[] { "输入兼容测试：不是真实遗漏提醒。", "一级测试", "二级测试", "三级测试" });
                Assert(WindowFocus.GetForegroundWindow() == foreground,
                    "Showing the notification preserves foreground");
                int styles = GetWindowLong(alert.Handle, -20 /* GWL_EXSTYLE */);
                Assert((styles & 0x08080020) == 0x08080020,
                    "Live toast has NOACTIVATE, LAYERED and TRANSPARENT styles");
                Assert(SendMessage(alert.Handle, 0x0021, IntPtr.Zero, IntPtr.Zero) == new IntPtr(3),
                    "Mouse activation is rejected");
                Assert(SendMessage(alert.Handle, 0x0084, IntPtr.Zero, IntPtr.Zero) == new IntPtr(-1),
                    "Mouse hit testing passes through the toast");
                // A hotkey received while another application is active must not open details.
                SendMessage(alert.Handle, 0x0312, new IntPtr(1), IntPtr.Zero);
                Assert(!Application.OpenForms.OfType<DetailForm>().Any(),
                    "Details remain closed when the game is not foreground");
                int samples = 0;
                int externalChanges = 0;
                var lastForeground = foreground;
                var deadline = Environment.TickCount64 + 10500;
                while (Environment.TickCount64 < deadline)
                {
                    await Task.Delay(25);
                    var current = WindowFocus.GetForegroundWindow();
                    if (current == alert.Handle)
                        throw new InvalidOperationException("Passive toast acquired foreground");
                    if (current != lastForeground) { externalChanges++; lastForeground = current; }
                    if (Application.OpenForms.OfType<DetailForm>().Any())
                        throw new InvalidOperationException("Details opened without an explicit request");
                    samples++;
                }
                Assert(!alert.Visible, "The notification closes automatically after ten seconds");
                report.Add($"PASS: Toast never acquired foreground throughout notification and expiry ({samples} samples)");
                report.Add($"NOTE: Foreground changes between other windows during test={externalChanges}");
                foreground = WindowFocus.GetForegroundWindow();
                alert.ShowAlert("overlay_demo_input_repeat", new[] { "再次显示测试提醒。", "一级测试", "二级测试", "三级测试" });
                AssertForeground(foreground);
                alert.Close();
                Assert(WindowFocus.GetForegroundWindow() == foreground,
                    "Showing again and exiting the overlay preserves foreground");
                exitCode = 0;
            }
            catch (Exception ex) { report.Add("FAIL: " + ex); }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "input-smoke.txt"), report);
                bool owned = !alert.IsDisposed && WindowFocus.GetForegroundWindow() == alert.Handle;
                if (!alert.IsDisposed) alert.Close();
                if (owned && WindowFocus.IsWindow(previous)) WindowFocus.SetForegroundWindow(previous);
            }
        };
        Application.Run(alert);
        return exitCode;

        void Assert(bool valid, string name)
        { if (!valid) throw new InvalidOperationException(name); report.Add("PASS: " + name); }

        void AssertForeground(IntPtr expected)
        {
            var actual = WindowFocus.GetForegroundWindow();
            if (actual != expected)
                throw new InvalidOperationException($"Foreground changed: expected={expected}, actual={actual}");
        }

        void CheckMenuGesture()
        {
            var gesture = new GamepadMenuGesture();
            Assert(!gesture.Update(0, true, true, 0) && !gesture.Update(0, true, true, 2000),
                "Menu already held before notification cannot open details");
            gesture.Update(0, true, false, 2100);
            Assert(!gesture.Update(0, true, true, 2200) && !gesture.Update(0, true, true, 3199),
                "A new Menu press shorter than one second cannot open details");
            Assert(gesture.Update(0, true, true, 3200) && !gesture.Update(0, true, true, 5000),
                "A deliberate one-second Menu hold opens details once per press");
            gesture.Update(0, true, false, 5100);
            gesture.Update(0, true, true, 5200);
            Assert(gesture.Update(0, true, true, 6200), "Releasing permits another deliberate Menu hold");
            gesture.Reset();
            gesture.Update(1, true, false, 0);
            gesture.Update(1, true, true, 100);
            gesture.Update(1, false, false, 500);
            Assert(!gesture.Update(1, true, true, 1500), "Disconnecting cancels an incomplete Menu hold");
            gesture.Reset();
            Assert(!gesture.Update(0, true, true, 10000), "Changing foreground cancels a stale Menu hold");
        }
    }
}
