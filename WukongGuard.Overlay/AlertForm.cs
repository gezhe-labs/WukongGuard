using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using RegretPill.Shared;

namespace WukongGuard.Overlay;

internal sealed class AlertForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int AlertTimeoutMilliseconds = 10000;
    private const int MoreHotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkG = 0x47;
    private readonly Label title = new();
    private readonly Label message = new();
    private readonly Label detail = new();
    private readonly Label countdown = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ToolStripMenuItem statusItem = new("状态：等待游戏消息") { Enabled = false };
    private readonly ToolStripMenuItem historyMenu = new("最近提醒");
    private readonly ToolStripMenuItem settingsMenu = new("黑神话：悟空 · 游戏配置");
    private bool gamepadEnabled;
    private readonly System.Windows.Forms.Timer countdownTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer gamepadTimer = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer gameWatchTimer = new() { Interval = 5000 };
    private readonly List<AlertEntry> history = new();
    private readonly Queue<AlertEntry> pendingAlerts = new();
    private readonly GamepadMenuGesture menuGesture = new();
    private string[] levels = Array.Empty<string>();
    private DetailForm? detailForm;
    private bool hotkeyRegistered;
    private string activeHotkey = "";
    private long dismissDeadline;
    private IntPtr cachedGameWindow;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // A passive layered toast must pass input to the game and never activate.
            parameters.ExStyle |= WsExNoActivate | WsExLayered | WsExTransparent;
            return parameters;
        }
    }

    internal AlertForm(bool exitWithGame = false, bool managedSession = false)
    {
        Text = "后悔药";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        Width = 630;
        Height = 164;
        BackColor = Color.FromArgb(27, 24, 20);
        ForeColor = Color.FromArgb(245, 229, 199);
        Font = new Font("Microsoft YaHei UI", 10f);
        Opacity = 0;

        title.Text = "后悔药  ·  防遗漏提醒";
        title.Font = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(225, 183, 104);
        title.SetBounds(20, 12, 500, 25);

        message.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        message.SetBounds(20, 42, 588, 51);

        detail.Text = "按 Ctrl+Shift+G 查看更多提示";
        detail.ForeColor = Color.FromArgb(173, 163, 145);
        detail.SetBounds(20, 97, 590, 23);

        countdown.Text = $"弹窗 {AlertTimeoutMilliseconds / 1000} 秒后自动关闭";
        countdown.Font = new Font(Font.FontFamily, 9f);
        countdown.ForeColor = Color.FromArgb(195, 174, 140);
        countdown.SetBounds(20, 127, 350, 24);

        Controls.AddRange(new Control[] { title, message, detail, countdown });
        Resize += (_, _) => SetRoundedRegion(this, 18);
        SetRoundedRegion(this, 18);
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(statusItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("打开后悔药", null, (_, _) => SignalLauncher(@"Local\RegretPillOpenPanel"));
        historyMenu.Enabled = false;
        trayMenu.Items.Add(historyMenu);
        gamepadEnabled = SettingsStore.LoadGamepadMenuHold();
        settingsMenu.Click += (_, _) => SignalLauncher(@"Local\RegretPillOpenSettings");
        trayMenu.Items.Add(settingsMenu);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出后悔药", null, (_, _) =>
        {
            SignalLauncher(@"Local\RegretPillQuit");
            Close();
        });
        trayIcon.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Shield;
        trayIcon.Text = "后悔药 · 游戏防遗漏提醒";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = !managedSession;
        LoadHistory();
        countdownTimer.Tick += (_, _) => UpdateCountdown();
        gamepadTimer.Tick += (_, _) => PollGamepadMenu();
        if (exitWithGame)
        {
            gameWatchTimer.Tick += (_, _) =>
            {
                try
                {
                    var games = System.Diagnostics.Process.GetProcessesByName("b1-Win64-Shipping");
                    bool running = games.Length > 0;
                    foreach (var game in games) game.Dispose();
                    var lease = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "WukongGuard", "active-session.txt");
                    if (!running || !File.Exists(lease)) Close();
                }
                catch (Exception) { /* Retry on the next tick. */ }
            };
            gameWatchTimer.Start();
        }
        FormClosed += (_, _) =>
        {
            StopInputListening();
            countdownTimer.Stop();
            countdownTimer.Dispose();
            gamepadTimer.Dispose();
            gameWatchTimer.Stop();
            gameWatchTimer.Dispose();
            detailForm?.Close();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
        };
        Shown += (_, _) => HideAlert();
    }

    private bool IsToastVisible => Visible && Opacity > 0 && detailForm == null;

    internal void ShowAlert(string id, string[] spoilerLevels)
    {
        if (spoilerLevels.Length != 4) return;
        if (IsToastVisible || detailForm != null)
        {
            RememberAlert(id, spoilerLevels);
            pendingAlerts.Enqueue(new AlertEntry(id, (string[])spoilerLevels.Clone()));
            return;
        }
        ShowAlertCore(id, spoilerLevels, true);
    }

    internal void SetStatus(string status)
    {
        statusItem.Text = "状态：" + status;
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WukongGuard");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "runtime-status.txt"),
                DateTime.UtcNow.Ticks + "|" + status);
        }
        catch (Exception) { /* The in-game status remains available in the tray. */ }
    }

    internal void ClearHistory()
    {
        history.Clear();
        RebuildHistoryMenu();
    }

    private void ShowAlertCore(string id, string[] spoilerLevels, bool remember)
    {
        if (spoilerLevels.Length != 4) return;
        if (remember) RememberAlert(id, spoilerLevels);
        gamepadEnabled = SettingsStore.LoadGamepadMenuHold();
        StopInputListening();
        countdownTimer.Stop();
        levels = (string[])spoilerLevels.Clone();
        bool preview = id.StartsWith("preview_", StringComparison.Ordinal);
        bool autoExperience = id.StartsWith("auto_experience_", StringComparison.Ordinal);
        bool hiddenArea = id.StartsWith("hidden_", StringComparison.Ordinal);
        title.Text = autoExperience ? "后悔药  ·  自动体验提醒"
            : preview ? "后悔药  ·  提醒预览"
            : hiddenArea ? "后悔药  ·  隐藏地区提示"
            : "后悔药  ·  不可补救提醒";
        message.Text = levels[0];
        var screen = Screen.FromHandle(GetGameWindowOrSelf()).Bounds;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + 80);
        Opacity = 0.96;
        var before = WindowFocus.GetForegroundWindow();
        Show();
        // BringToFront on a top-level Form can activate it. Raise without activation.
        bool raised = SetWindowPos(Handle, new IntPtr(-1) /* HWND_TOPMOST */, 0, 0, 0, 0,
            0x0001 | 0x0002 | 0x0010 /* NOSIZE | NOMOVE | NOACTIVATE */);
        OverlayLog.Write($"toast shown window={Handle} foreground_before={before} "
            + $"foreground_after={WindowFocus.GetForegroundWindow()} raised={raised}");
        StartInputListening();
        RestartCountdown();
    }

    private void RememberAlert(string id, string[] spoilerLevels)
    {
        history.Insert(0, new AlertEntry(id, (string[])spoilerLevels.Clone()));
        if (history.Count > 20) history.RemoveAt(history.Count - 1);
        if (!id.StartsWith("overlay_demo", StringComparison.Ordinal)
            && !id.StartsWith("preview_", StringComparison.Ordinal))
        {
            try { HistoryStore.Add(id, spoilerLevels); }
            catch (Exception) { /* History must not prevent delivery. */ }
        }
        RebuildHistoryMenu();
    }

    private void LoadHistory()
    {
        try
        {
            foreach (var entry in HistoryStore.Load().Take(20))
                history.Add(new AlertEntry(entry.Id, entry.Levels, entry.SeenAt));
            RebuildHistoryMenu();
        }
        catch (Exception) { }
    }

    private void RebuildHistoryMenu()
    {
        historyMenu.DropDownItems.Clear();
        foreach (var entry in history)
        {
            var item = new ToolStripMenuItem(entry.SeenAt.ToString("HH:mm:ss") + "  " + entry.Levels[0]);
            item.Click += (_, _) => OpenHistoryDetail(entry);
            historyMenu.DropDownItems.Add(item);
        }
        historyMenu.Enabled = true;
    }

    private sealed class AlertEntry
    {
        internal readonly string Id;
        internal readonly string[] Levels;
        internal readonly DateTime SeenAt;

        internal AlertEntry(string id, string[] levels, DateTime? seenAt = null)
        {
            Id = id;
            Levels = levels;
            SeenAt = seenAt ?? DateTime.Now;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021 /* WM_MOUSEACTIVATE */)
        {
            message.Result = new IntPtr(3); // MA_NOACTIVATE
            return;
        }
        if (message.Msg == 0x0084 /* WM_NCHITTEST */)
        {
            message.Result = new IntPtr(-1); // HTTRANSPARENT
            return;
        }
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == MoreHotkeyId)
        {
            if (IsGameForeground()) OpenDetail("keyboard");
            return;
        }
        base.WndProc(ref message);
    }

    private void StartInputListening()
    {
        StopInputListening();
        var preferred = SettingsStore.LoadMoreHotkey();
        string[] attempts = preferred == "CtrlAltShiftG"
            ? new[] { "CtrlAltShiftG", "CtrlShiftG" }
            : new[] { "CtrlShiftG", "CtrlAltShiftG" };
        foreach (var attempt in attempts)
        {
            uint modifiers = ModControl | ModShift | ModNoRepeat;
            if (attempt == "CtrlAltShiftG") modifiers |= ModAlt;
            if (!RegisterHotKey(Handle, MoreHotkeyId, modifiers, VkG)) continue;
            hotkeyRegistered = true;
            activeHotkey = attempt == "CtrlAltShiftG" ? "Ctrl+Alt+Shift+G" : "Ctrl+Shift+G";
            break;
        }
        if (gamepadEnabled) gamepadTimer.Start();
        UpdateInputHint();
    }

    private void StopInputListening()
    {
        gamepadTimer.Stop();
        menuGesture.Reset();
        if (hotkeyRegistered && IsHandleCreated)
            UnregisterHotKey(Handle, MoreHotkeyId);
        hotkeyRegistered = false;
        activeHotkey = "";
    }

    private void UpdateInputHint()
    {
        string keyboard = activeHotkey.Length > 0
            ? $"按 {activeHotkey} 查看更多"
            : "键盘快捷键被占用 · 可从托盘“最近提醒”查看";
        string gamepad = gamepadEnabled ? " · 手柄长按 Menu 1 秒" : "";
        detail.Text = keyboard + gamepad;
    }

    private void PollGamepadMenu()
    {
        if (!IsToastVisible || !gamepadEnabled) return;
        if (!IsGameForeground()) { menuGesture.Reset(); return; }
        long now = Environment.TickCount64;
        for (uint index = 0; index < 4; index++)
        {
            bool connected = GamepadInput.TryGetButtons(index, out var buttons);
            if (!menuGesture.Update((int)index, connected, (buttons & GamepadInput.Menu) != 0, now)) continue;
            OpenDetail("gamepad_menu_hold");
            return;
        }
    }

    private void OpenHistoryDetail(AlertEntry entry)
    {
        if (detailForm != null) { detailForm.Activate(); return; }
        string heading = entry.Id.StartsWith("hidden_", StringComparison.Ordinal)
            ? "后悔药  ·  隐藏地区提示" : "后悔药  ·  历史提醒";
        ShowDetail(heading, entry.Levels, "history");
    }

    private void OpenDetail(string origin)
    {
        if (!IsToastVisible) return;
        ShowDetail(title.Text, levels, origin);
    }

    private void ShowDetail(string heading, string[] spoilerLevels, string origin)
    {
        var gameWindow = GetGameWindowOrSelf();
        bool returnToGame = gameWindow != Handle && WindowFocus.IsForeground(gameWindow);
        OverlayLog.Write($"detail requested origin={origin} game={gameWindow} "
            + $"foreground={WindowFocus.GetForegroundWindow()} return_to_game={returnToGame}");
        var screen = Screen.FromHandle(gameWindow).Bounds;
        HideAlert(false);
        var dialog = new DetailForm(heading, spoilerLevels, screen, gameWindow, returnToGame);
        detailForm = dialog;
        dialog.FormClosed += (_, _) =>
        {
            detailForm = null;
            if (!IsDisposed && !Disposing && IsHandleCreated && pendingAlerts.Count > 0)
                BeginInvoke(new Action(ShowNextPending));
        };
        dialog.Show();
    }

    private void HideAlert(bool showNext = true)
    {
        bool wasVisible = IsToastVisible;
        countdownTimer.Stop();
        StopInputListening();
        Hide();
        if (wasVisible) OverlayLog.Write($"toast hidden window={Handle} foreground={WindowFocus.GetForegroundWindow()}");
        if (showNext && !IsDisposed && pendingAlerts.Count > 0)
            BeginInvoke(new Action(ShowNextPending));
    }

    private void ShowNextPending()
    {
        if (IsDisposed || detailForm != null || IsToastVisible || pendingAlerts.Count == 0) return;
        var next = pendingAlerts.Dequeue();
        ShowAlertCore(next.Id, next.Levels, false);
    }

    private void RestartCountdown()
    {
        countdownTimer.Stop();
        dismissDeadline = Stopwatch.GetTimestamp() + AlertTimeoutMilliseconds * Stopwatch.Frequency / 1000;
        UpdateCountdown();
        countdownTimer.Start();
    }

    private void UpdateCountdown()
    {
        double remaining = (dismissDeadline - Stopwatch.GetTimestamp())
            * 1000.0 / Stopwatch.Frequency;
        if (remaining <= 0)
        {
            HideAlert();
            return;
        }
        int seconds = (int)Math.Ceiling(remaining / 1000.0);
        countdown.Text = $"弹窗 {seconds} 秒后自动关闭";
    }

    private static void SetRoundedRegion(Control control, int radius)
    {
        if (control.Width <= 0 || control.Height <= 0) return;
        int diameter = Math.Min(radius * 2, Math.Min(control.Width, control.Height));
        using var path = new GraphicsPath();
        var bounds = new Rectangle(0, 0, control.Width, control.Height);
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        var previous = control.Region;
        control.Region = new Region(path);
        previous?.Dispose();
    }

    private IntPtr GetGameWindowOrSelf()
    {
        if (WindowFocus.IsWindow(cachedGameWindow)) return cachedGameWindow;
        var games = System.Diagnostics.Process.GetProcessesByName("b1-Win64-Shipping");
        try
        {
            foreach (var game in games)
                if (game.MainWindowHandle != IntPtr.Zero) return cachedGameWindow = game.MainWindowHandle;
            return Handle;
        }
        finally { foreach (var game in games) game.Dispose(); }
    }

    private bool IsGameForeground()
    {
        var game = GetGameWindowOrSelf();
        return game != Handle && WindowFocus.IsForeground(game);
    }

    private static void SignalLauncher(string eventName)
    {
        try { using var signal = EventWaitHandle.OpenExisting(eventName); signal.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }

}
