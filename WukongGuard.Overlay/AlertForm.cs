using System.Drawing;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WukongGuard.Overlay;

internal sealed class AlertForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int AlertTimeoutMilliseconds = 10000;
    private const int MoreHotkeyId = 1;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkG = 0x47;
    private const int GamepadMenuHoldMilliseconds = 1000;
    private readonly Label title = new();
    private readonly Label message = new();
    private readonly Label detail = new();
    private readonly Label countdown = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ToolStripMenuItem statusItem = new("状态：等待游戏消息") { Enabled = false };
    private readonly ToolStripMenuItem historyMenu = new("最近提醒");
    private readonly ToolStripMenuItem settingsMenu = new("设置");
    private readonly ToolStripMenuItem hiddenAreaItem = new("隐藏地区提示（默认关闭）")
    {
        CheckOnClick = true
    };
    private readonly ToolStripMenuItem hotkeyPrimaryItem = new("Ctrl+Shift+G") { CheckOnClick = true };
    private readonly ToolStripMenuItem hotkeyFallbackItem = new("Ctrl+Alt+Shift+G") { CheckOnClick = true };
    private readonly ToolStripMenuItem gamepadMenuItem = new("手柄长按 Menu 查看详情") { CheckOnClick = true };
    private readonly System.Windows.Forms.Timer countdownTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer gamepadTimer = new() { Interval = 50 };
    private readonly System.Windows.Forms.Timer gameWatchTimer = new() { Interval = 5000 };
    private readonly List<AlertEntry> history = new();
    private readonly Queue<AlertEntry> pendingAlerts = new();
    private readonly long[] menuPressedAt = new long[4];
    private readonly bool[] menuConsumed = new bool[4];
    private string[] levels = Array.Empty<string>();
    private DetailForm? detailForm;
    private bool hotkeyRegistered;
    private string activeHotkey = "";
    private long dismissDeadline;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExNoActivate;
            return parameters;
        }
    }

    internal AlertForm(bool exitWithGame = false)
    {
        Text = "WukongGuard";
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

        title.Text = "WUKONGGUARD  ·  防遗漏提醒";
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
        foreach (var control in new Control[] { this, title, message, detail, countdown })
            control.Click += (_, _) => OpenDetail();
        Resize += (_, _) => SetRoundedRegion(this, 18);
        SetRoundedRegion(this, 18);
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(statusItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        historyMenu.Enabled = false;
        trayMenu.Items.Add(historyMenu);
        trayMenu.Items.Add("测试提示交互", null, (_, _) => ShowAlert("overlay_demo", new[]
        {
            "显示测试：这不是当前位置的遗漏提醒。",
            "一级提示：已进入可操作的详情窗口。",
            "二级提示：键盘与手柄都可以逐级展开。",
            "三级提示：测试完成，可以关闭详情窗口。"
        }));
        hiddenAreaItem.Checked = SettingsStore.LoadHiddenAreaHints();
        hiddenAreaItem.CheckedChanged += OnHiddenAreaSettingChanged;
        settingsMenu.DropDownItems.Add(hiddenAreaItem);
        settingsMenu.DropDownItems.Add(new ToolStripSeparator());
        var hotkeyMenu = new ToolStripMenuItem("查看更多快捷键");
        var preferredHotkey = SettingsStore.LoadMoreHotkey();
        hotkeyPrimaryItem.Checked = preferredHotkey == "CtrlShiftG";
        hotkeyFallbackItem.Checked = preferredHotkey == "CtrlAltShiftG";
        hotkeyPrimaryItem.Click += (_, _) => ChooseHotkey("CtrlShiftG");
        hotkeyFallbackItem.Click += (_, _) => ChooseHotkey("CtrlAltShiftG");
        hotkeyMenu.DropDownItems.Add(hotkeyPrimaryItem);
        hotkeyMenu.DropDownItems.Add(hotkeyFallbackItem);
        settingsMenu.DropDownItems.Add(hotkeyMenu);
        gamepadMenuItem.Checked = SettingsStore.LoadGamepadMenuHold();
        gamepadMenuItem.CheckedChanged += OnGamepadMenuSettingChanged;
        settingsMenu.DropDownItems.Add(gamepadMenuItem);
        trayMenu.Items.Add(settingsMenu);
        trayMenu.Items.Add("清空提醒记录", null, (_, _) =>
        {
            history.Clear();
            historyMenu.DropDownItems.Clear();
            historyMenu.Enabled = false;
        });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出 WukongGuard", null, (_, _) => Close());
        trayIcon.Icon = SystemIcons.Shield;
        trayIcon.Text = "WukongGuard Overlay";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
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
                    if (!running) Close();
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
    }

    private void OnHiddenAreaSettingChanged(object? sender, EventArgs e)
    {
        try
        {
            SettingsStore.SaveHiddenAreaHints(hiddenAreaItem.Checked);
        }
        catch (Exception ex)
        {
            hiddenAreaItem.CheckedChanged -= OnHiddenAreaSettingChanged;
            hiddenAreaItem.Checked = !hiddenAreaItem.Checked;
            hiddenAreaItem.CheckedChanged += OnHiddenAreaSettingChanged;
            MessageBox.Show("无法保存设置：" + ex.Message, "WukongGuard");
        }
    }

    private void OnGamepadMenuSettingChanged(object? sender, EventArgs e)
    {
        try { SettingsStore.SaveGamepadMenuHold(gamepadMenuItem.Checked); }
        catch (Exception ex)
        {
            gamepadMenuItem.CheckedChanged -= OnGamepadMenuSettingChanged;
            gamepadMenuItem.Checked = !gamepadMenuItem.Checked;
            gamepadMenuItem.CheckedChanged += OnGamepadMenuSettingChanged;
            MessageBox.Show("无法保存设置：" + ex.Message, "WukongGuard");
        }
        if (!gamepadMenuItem.Checked) gamepadTimer.Stop();
        else if (IsToastVisible) gamepadTimer.Start();
        UpdateInputHint();
    }

    private void ShowAlertCore(string id, string[] spoilerLevels, bool remember)
    {
        if (spoilerLevels.Length != 4) return;
        if (remember) RememberAlert(id, spoilerLevels);
        StopInputListening();
        countdownTimer.Stop();
        levels = (string[])spoilerLevels.Clone();
        bool preview = id.StartsWith("preview_", StringComparison.Ordinal);
        bool autoExperience = id.StartsWith("auto_experience_", StringComparison.Ordinal);
        bool hiddenArea = id.StartsWith("hidden_", StringComparison.Ordinal);
        title.Text = autoExperience ? "WUKONGGUARD  ·  自动体验提醒"
            : preview ? "WUKONGGUARD  ·  提醒预览"
            : hiddenArea ? "WUKONGGUARD  ·  隐藏地区提示"
            : "WUKONGGUARD  ·  不可补救提醒";
        message.Text = levels[0];
        var screen = Screen.FromHandle(GetGameWindowOrSelf()).Bounds;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + 80);
        Opacity = 0.96;
        Show();
        BringToFront();
        StartInputListening();
        RestartCountdown();
    }

    private void RememberAlert(string id, string[] spoilerLevels)
    {
        history.Insert(0, new AlertEntry(id, (string[])spoilerLevels.Clone()));
        if (history.Count > 20) history.RemoveAt(history.Count - 1);
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
        internal readonly DateTime SeenAt = DateTime.Now;

        internal AlertEntry(string id, string[] levels)
        {
            Id = id;
            Levels = levels;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey && message.WParam.ToInt32() == MoreHotkeyId)
        {
            OpenDetail();
            return;
        }
        base.WndProc(ref message);
    }

    private void ChooseHotkey(string hotkey)
    {
        try
        {
            SettingsStore.SaveMoreHotkey(hotkey);
            hotkeyPrimaryItem.Checked = hotkey == "CtrlShiftG";
            hotkeyFallbackItem.Checked = hotkey == "CtrlAltShiftG";
            if (IsToastVisible) StartInputListening();
        }
        catch (Exception ex)
        {
            var previous = SettingsStore.LoadMoreHotkey();
            hotkeyPrimaryItem.Checked = previous == "CtrlShiftG";
            hotkeyFallbackItem.Checked = previous == "CtrlAltShiftG";
            MessageBox.Show("无法保存设置：" + ex.Message, "WukongGuard");
        }
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
        if (gamepadMenuItem.Checked) gamepadTimer.Start();
        UpdateInputHint();
    }

    private void StopInputListening()
    {
        gamepadTimer.Stop();
        Array.Clear(menuPressedAt);
        Array.Clear(menuConsumed);
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
        string gamepad = gamepadMenuItem.Checked ? " · 手柄长按 Menu 1 秒" : "";
        detail.Text = keyboard + gamepad;
    }

    private void PollGamepadMenu()
    {
        if (!IsToastVisible || !gamepadMenuItem.Checked) return;
        long now = Stopwatch.GetTimestamp();
        for (uint index = 0; index < 4; index++)
        {
            bool held = GamepadInput.TryGetButtons(index, out var buttons)
                && (buttons & GamepadInput.Menu) != 0;
            if (!held)
            {
                menuPressedAt[index] = 0;
                menuConsumed[index] = false;
                continue;
            }
            if (menuPressedAt[index] == 0) menuPressedAt[index] = now;
            if (menuConsumed[index] || (now - menuPressedAt[index]) * 1000.0
                / Stopwatch.Frequency < GamepadMenuHoldMilliseconds) continue;
            menuConsumed[index] = true;
            OpenDetail();
            return;
        }
    }

    private void OpenHistoryDetail(AlertEntry entry)
    {
        if (detailForm != null) { detailForm.Activate(); return; }
        string heading = entry.Id.StartsWith("hidden_", StringComparison.Ordinal)
            ? "WUKONGGUARD  ·  隐藏地区提示" : "WUKONGGUARD  ·  历史提醒";
        ShowDetail(heading, entry.Levels);
    }

    private void OpenDetail()
    {
        if (!IsToastVisible) return;
        ShowDetail(title.Text, levels);
    }

    private void ShowDetail(string heading, string[] spoilerLevels)
    {
        var gameWindow = GetGameWindowOrSelf();
        bool returnToGame = gameWindow != Handle && GetForegroundWindow() == gameWindow;
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
        countdownTimer.Stop();
        StopInputListening();
        Hide();
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
        var games = System.Diagnostics.Process.GetProcessesByName("b1-Win64-Shipping");
        try { return games.FirstOrDefault()?.MainWindowHandle ?? Handle; }
        finally { foreach (var game in games) game.Dispose(); }
    }

}
