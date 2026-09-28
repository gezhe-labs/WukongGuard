using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;

namespace WukongGuard.Overlay;

internal sealed class AlertForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int AlertTimeoutMilliseconds = 10000;
    private readonly Label title = new();
    private readonly Label message = new();
    private readonly Label detail = new();
    private readonly Label countdown = new();
    private readonly Button more = new();
    private readonly Button dismiss = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ToolStripMenuItem statusItem = new("状态：等待游戏消息") { Enabled = false };
    private readonly ToolStripMenuItem historyMenu = new("最近提醒");
    private readonly ToolStripMenuItem settingsMenu = new("设置");
    private readonly ToolStripMenuItem hiddenAreaItem = new("隐藏地区提示（默认关闭）")
    {
        CheckOnClick = true
    };
    private readonly System.Windows.Forms.Timer countdownTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer gameWatchTimer = new() { Interval = 5000 };
    private readonly List<AlertEntry> history = new();
    private readonly Queue<AlertEntry> pendingAlerts = new();
    private string[] levels = Array.Empty<string>();
    private int level;
    private bool preview;
    private bool autoExperience;
    private long dismissDeadline;

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

        detail.Text = "提示级别 0 · 点击“更多提示”逐步揭示";
        detail.ForeColor = Color.FromArgb(173, 163, 145);
        detail.SetBounds(20, 97, 440, 23);

        countdown.Text = $"弹窗 {AlertTimeoutMilliseconds / 1000} 秒后自动关闭";
        countdown.Font = new Font(Font.FontFamily, 9f);
        countdown.ForeColor = Color.FromArgb(195, 174, 140);
        countdown.SetBounds(20, 127, 260, 24);

        more.Text = "更多提示";
        more.SetBounds(415, 124, 105, 30);
        more.FlatStyle = FlatStyle.Flat;
        more.FlatAppearance.BorderSize = 0;
        more.FlatAppearance.BorderColor = Color.FromArgb(185, 143, 77);
        more.FlatAppearance.MouseOverBackColor = Color.FromArgb(91, 69, 42);
        more.BackColor = Color.FromArgb(67, 51, 33);
        more.ForeColor = ForeColor;
        more.UseVisualStyleBackColor = false;
        more.Paint += (_, e) => DrawRoundedButtonBorder(more, e, 10);
        more.Click += (_, _) => RevealNext();

        dismiss.Text = "关闭";
        dismiss.SetBounds(528, 124, 80, 30);
        dismiss.FlatStyle = FlatStyle.Flat;
        dismiss.FlatAppearance.BorderSize = 0;
        dismiss.FlatAppearance.BorderColor = Color.FromArgb(125, 111, 91);
        dismiss.FlatAppearance.MouseOverBackColor = Color.FromArgb(70, 63, 53);
        dismiss.BackColor = Color.FromArgb(49, 44, 37);
        dismiss.ForeColor = ForeColor;
        dismiss.UseVisualStyleBackColor = false;
        dismiss.Paint += (_, e) => DrawRoundedButtonBorder(dismiss, e, 10);
        dismiss.Click += (_, _) => HideAlert();

        Controls.AddRange(new Control[] { title, message, detail, countdown, more, dismiss });
        Resize += (_, _) => SetRoundedRegion(this, 18);
        more.Resize += (_, _) => SetRoundedRegion(more, 10);
        dismiss.Resize += (_, _) => SetRoundedRegion(dismiss, 10);
        SetRoundedRegion(this, 18);
        SetRoundedRegion(more, 10);
        SetRoundedRegion(dismiss, 10);
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(statusItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        historyMenu.Enabled = false;
        trayMenu.Items.Add(historyMenu);
        hiddenAreaItem.Checked = SettingsStore.LoadHiddenAreaHints();
        hiddenAreaItem.CheckedChanged += OnHiddenAreaSettingChanged;
        settingsMenu.DropDownItems.Add(hiddenAreaItem);
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
            countdownTimer.Stop();
            countdownTimer.Dispose();
            gameWatchTimer.Stop();
            gameWatchTimer.Dispose();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayMenu.Dispose();
        };
        Shown += (_, _) => HideAlert();
    }

    internal void ShowAlert(string id, string[] spoilerLevels)
    {
        if (spoilerLevels.Length != 4) return;
        if (Visible && Opacity > 0)
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

    private void ShowAlertCore(string id, string[] spoilerLevels, bool remember)
    {
        if (spoilerLevels.Length != 4) return;
        if (remember) RememberAlert(id, spoilerLevels);
        countdownTimer.Stop();
        levels = spoilerLevels;
        level = 0;
        preview = id.StartsWith("preview_", StringComparison.Ordinal);
        autoExperience = id.StartsWith("auto_experience_", StringComparison.Ordinal);
        bool hiddenArea = id.StartsWith("hidden_", StringComparison.Ordinal);
        title.Text = autoExperience ? "WUKONGGUARD  ·  自动体验提醒"
            : preview ? "WUKONGGUARD  ·  提醒预览"
            : hiddenArea ? "WUKONGGUARD  ·  隐藏地区提示"
            : "WUKONGGUARD  ·  不可补救提醒";
        message.Text = levels[0];
        detail.Text = autoExperience
            ? "体验触发 · 未校准，不表示当前位置有遗漏"
            : preview
            ? "手动预览 · 非自动触发 · Ctrl+F7 切换"
            : id == "overlay_demo"
            ? "显示测试 · 不表示附近有遗漏点"
            : hiddenArea
            ? "可回访内容 · 托盘“设置”中可关闭"
            : "提示级别 0 · 点击“更多提示”逐步揭示";
        more.Enabled = true;
        var screen = Screen.FromHandle(GetGameWindowOrSelf()).Bounds;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + 80);
        Opacity = 0.96;
        Show();
        BringToFront();
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
            item.Click += (_, _) => ShowAlertCore(entry.Id, entry.Levels, false);
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

    private void HideAlert()
    {
        countdownTimer.Stop();
        Hide();
        if (pendingAlerts.Count > 0 && !IsDisposed)
        {
            var next = pendingAlerts.Dequeue();
            BeginInvoke(new Action(() => ShowAlertCore(next.Id, next.Levels, false)));
        }
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

    private static void DrawRoundedButtonBorder(Button button, PaintEventArgs e, int radius)
    {
        if (button.Width <= 1 || button.Height <= 1) return;
        var bounds = new RectangleF(0.5f, 0.5f, button.Width - 1f, button.Height - 1f);
        float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        using var pen = new Pen(button.FlatAppearance.BorderColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);
    }

    private IntPtr GetGameWindowOrSelf()
    {
        var games = System.Diagnostics.Process.GetProcessesByName("b1-Win64-Shipping");
        try { return games.FirstOrDefault()?.MainWindowHandle ?? Handle; }
        finally { foreach (var game in games) game.Dispose(); }
    }

    private void RevealNext()
    {
        if (level >= 3) return;
        level++;
        message.Text = levels[level];
        detail.Text = autoExperience
            ? $"体验触发 · 提示级别 {level} · 不表示当前位置有遗漏"
            : preview
            ? $"手动预览 · 提示级别 {level} · Ctrl+F7 切换"
            : $"提示级别 {level}";
        more.Enabled = level < 3;
        RestartCountdown();
    }
}
