using System.Drawing.Imaging;

namespace WukongGuard.Installer;

internal static class UiSmoke
{
    // Run against a prepared, inert fixture only. Never launch or attach to a game.
    internal static int Run(string root, string output)
    {
        var fakeGame = Path.Combine(root, "b1", "Binaries", "Win64", "b1-Win64-Shipping.exe");
        if (!File.Exists(fakeGame) || new FileInfo(fakeGame).Length > 100
            || !File.ReadAllText(fakeGame).Contains("fixture") || SessionControl.IsGameRunning)
            throw new InvalidOperationException("UI smoke requires an inert fixture and a closed real game.");
        Directory.CreateDirectory(output);
        SessionControl.SetSmokeSessionFile(root);
        RegretPill.Shared.HistoryStore.SmokeDirectory = Path.Combine(root, ".ui-profile");
        UserPreferences.SmokeDirectory = Path.Combine(root, ".ui-profile");
        Directory.CreateDirectory(UserPreferences.SmokeDirectory);
        UserPreferences.SaveSkipCloseToTrayPrompt(false);
        RegretPill.Shared.HistoryStore.Clear();
        Installation.InstallAsync(root, _ => { }).GetAwaiter().GetResult();
        using var open = new EventWaitHandle(false, EventResetMode.AutoReset);
        using var settings = new EventWaitHandle(false, EventResetMode.AutoReset);
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset);
        using var form = new LauncherForm(open, settings, quit, root) { Opacity = 0, ShowInTaskbar = false };
        var report = new List<string>();
        int exitCode = 1;
        form.Shown += async (_, _) =>
        {
            try
            {
                Assert(!form.IsArmed && !SessionControl.IsActive(root) && !File.Exists(SessionControl.SessionFile),
                    "Opening EXE does not arm protection");
                Assert(form.FooterRightAligned, "Button and guidance align with the game card right edge");
                VerifyButtons(form, "homepage");
                Capture(form, "home-idle.png");
                foreach (var stage in new[] { LauncherStage.Preparing, LauncherStage.Ready, LauncherStage.Connecting,
                    LauncherStage.Running, LauncherStage.RuntimeIssue, LauncherStage.Failed, LauncherStage.GameEnded })
                {
                    form.PreviewStage(stage); Capture(form, "home-" + stage.ToString().ToLowerInvariant() + ".png");
                }
                form.PreviewStage(LauncherStage.Idle);
                form.ClientSize = new Size(800, 650);
                Capture(form, "home-compact.png");
                Assert(form.FooterRightAligned, "Compact layout retains right alignment");
                form.ClientSize = new Size(1120, 740);
                foreach (var scale in new[] { 1.25f, 1.5f })
                {
                    UiStyle.PreviewScale = scale;
                    using var scaled = new LauncherForm(open, settings, quit, root) { Opacity = 0, ShowInTaskbar = false };
                    scaled.Show();
                    VerifyButtons(scaled, "homepage at " + (int)(scale * 100) + "%");
                    Capture(scaled, "home-scale-" + (int)(scale * 100) + ".png");
                    Assert(scaled.FooterRightAligned, "Scaled layout retains right alignment at " + (int)(scale * 100) + "%");
                    scaled.RequestExit();
                    using var scaledConfig = new GameConfigurationForm(() => "保护尚未启动", () => false, () => { }, () => { })
                        { Opacity = 0, ShowInTaskbar = false };
                    scaledConfig.Show();
                    VerifyButtons(scaledConfig, "configuration at " + (int)(scale * 100) + "%");
                    Capture(scaledConfig, "config-scale-" + (int)(scale * 100) + ".png");
                    scaledConfig.Close();
                }
                UiStyle.PreviewScale = 1f;
                report.Add("PASS: rendered compact, 125% and 150% font and layout metrics");
                using (var config = new GameConfigurationForm(() => "保护尚未启动\n先启用保护，再从 Steam 启动游戏。",
                    () => false, () => { }, () => { }) { Opacity = 0, ShowInTaskbar = false })
                {
                    config.Show(form);
                    RegretPill.Shared.HistoryStore.Add("ui_sample", new[] { "测试记录：关键节点前的提醒。", "一级提示", "二级提示", "三级提示" });
                    for (int page = 0; page < 3; page++)
                    { config.PreviewPage(page); VerifyButtons(config, "configuration page " + page); Capture(config, "config-" + page + ".png"); }
                    using (var detail = new HistoryDetailForm(RegretPill.Shared.HistoryStore.Load()[0])
                        { Opacity = 0, ShowInTaskbar = false })
                    {
                        detail.Show(config);
                        VerifyButtons(detail, "history detail");
                        Capture(detail, "history-detail.png");
                        detail.Close();
                    }
                    config.ClientSize = new Size(740, 610);
                    config.PreviewPage(0); Capture(config, "config-compact.png");
                    config.Close();
                }
                form.PrimaryButton.PerformClick();
                await Until(() => form.IsArmed || form.CurrentStage == LauncherStage.Failed);
                Assert(form.IsArmed && form.CurrentStage == LauncherStage.Ready
                    && SessionControl.IsActive(root) && File.Exists(SessionControl.SessionFile),
                    "Explicit start prepares and arms one session");
                SessionControl.CleanupDisabled(root);
                Assert(SessionControl.IsActive(root) && File.Exists(SessionControl.SessionFile),
                    "Previous cleanup cannot revoke the current session");
                int promptsShown = 0;
                form.ClosePromptCreated = prompt =>
                {
                    promptsShown++;
                    prompt.Opacity = 0;
                    prompt.Shown += (_, _) => prompt.BeginInvoke(new Action(() =>
                    {
                        Capture(prompt, "close-to-tray.png");
                        VerifyButtons(prompt, "close prompt");
                        prompt.DoNotRemind.Checked = true;
                        prompt.CancelAction.PerformClick();
                    }));
                };
                form.Close();
                Assert(form.Visible && form.IsArmed && promptsShown == 1
                    && !UserPreferences.LoadSkipCloseToTrayPrompt(),
                    "Cancel keeps the window open and does not save the checkbox");
                form.ClosePromptCreated = prompt =>
                {
                    promptsShown++;
                    prompt.Opacity = 0;
                    prompt.Shown += (_, _) => prompt.BeginInvoke(new Action(() => prompt.HideAction.PerformClick()));
                };
                form.Close();
                Assert(!form.Visible && !form.IsDisposed && form.IsArmed && promptsShown == 2
                    && !UserPreferences.LoadSkipCloseToTrayPrompt(),
                    "Confirm hides to tray and preserves protection; unchecked prompt remains enabled");
                open.Set(); await Until(() => form.Visible);
                Assert(form.IsArmed, "Reopening window preserves the active session");
                form.ClosePromptCreated = prompt =>
                {
                    promptsShown++;
                    prompt.Opacity = 0;
                    prompt.Shown += (_, _) => prompt.BeginInvoke(new Action(() =>
                    {
                        prompt.DoNotRemind.Checked = true;
                        prompt.HideAction.PerformClick();
                    }));
                };
                form.Close();
                Assert(!form.Visible && form.IsArmed && UserPreferences.LoadSkipCloseToTrayPrompt(),
                    "Do not remind is persisted after confirmation");
                using (var reopened = new LauncherForm(open, settings, quit, root) { Opacity = 0, ShowInTaskbar = false })
                {
                    reopened.ClosePromptCreated = _ => throw new InvalidOperationException("Saved preference was ignored.");
                    reopened.Show(); reopened.Close();
                    Assert(!reopened.Visible && !reopened.IsDisposed,
                        "A new launcher instance reads the saved preference and hides without a prompt");
                    reopened.RequestExit();
                }
                open.Set(); await Until(() => form.Visible);
                form.PrimaryButton.PerformClick();
                Assert(!form.IsArmed && !SessionControl.IsActive(root) && !File.Exists(SessionControl.SessionFile),
                    "Cancel waiting disarms the session");
                open.Set(); await Task.Delay(1200);
                Assert(!form.IsArmed && !SessionControl.IsActive(root), "Reopening idle EXE does not arm it");
                form.PrimaryButton.PerformClick(); await Until(() => form.IsArmed);
                exitCode = 0;
                form.RequestExit();
                Assert(!SessionControl.IsActive(root) && !File.Exists(SessionControl.SessionFile),
                    "Explicit exit revokes protection");
            }
            catch (Exception ex) { report.Add("FAIL: " + ex); exitCode = 1; }
            finally
            {
                File.WriteAllLines(Path.Combine(output, "ui-smoke.txt"), report);
                if (!form.IsDisposed) form.RequestExit();
            }
        };
        Application.Run(form);
        return exitCode;

        void Assert(bool valid, string name)
        { if (!valid) throw new InvalidOperationException(name); report.Add("PASS: " + name); }

        void VerifyButtons(Control window, string context)
        {
            int count = ButtonPaintChecks.Verify(window);
            report.Add($"PASS: {context}: {count} buttons repaint corners in normal/hover/focus/pressed/disabled states");
        }

        void Capture(Form window, string name)
        {
            window.PerformLayout(); window.Refresh();
            using var bitmap = new Bitmap(window.Width, window.Height);
            window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name), ImageFormat.Png);
        }
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("UI state did not change within 15 seconds.");
            await Task.Delay(50);
        }
    }
}
