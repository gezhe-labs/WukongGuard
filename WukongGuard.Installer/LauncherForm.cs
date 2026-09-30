using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;

namespace WukongGuard.Installer;

internal sealed class LauncherForm : Form
{
    private readonly EventWaitHandle openPanel, openSettings, quit;
    private readonly Panel header = new(), footer = new();
    private readonly CardListPanel cards = new();
    private readonly Label brand = UiStyle.Label("后悔药", 28, true);
    private readonly Label promise = UiStyle.Label("在错过之前提醒你，避免遗漏无法补救的支线与隐藏内容。", 11, false, UiStyle.Muted);
    private readonly Label libraryTitle = UiStyle.Label("支持的游戏", 12, true);
    private readonly Label version = UiStyle.Label(Installation.ProductVersion + " · 验证版", 9, false, UiStyle.Muted);
    private readonly GameCard gameCard = new();
    private readonly SurfacePanel upcoming = new();
    private readonly Label upcomingMark = UiStyle.Label("＋", 30, false, UiStyle.Accent);
    private readonly Label upcomingTitle = UiStyle.Label("更多游戏更新中", 14, true);
    private readonly Label upcomingHint = UiStyle.Label("新的冒险，也值得少一点遗憾。", 10, false, UiStyle.Muted);
    private readonly Label actionGuidance = UiStyle.Label("", 10, false, UiStyle.Muted);
    private readonly RoundedButton actionButton = UiStyle.Button("启动保护", true);
    private readonly LinkLabel stopLink = new() { Text = "停止保护", LinkColor = UiStyle.Muted,
        ActiveLinkColor = UiStyle.Accent, VisitedLinkColor = UiStyle.Muted, AutoSize = false, LinkBehavior = LinkBehavior.HoverUnderline };
    private readonly ProgressBar progress = new() { Style = ProgressBarStyle.Marquee, Visible = false };
    private readonly NotifyIcon tray = new();
    private readonly ToolStripMenuItem trayStatus = new() { Enabled = false };
    private readonly ToolStripMenuItem trayStop = new("停止保护");
    private readonly System.Windows.Forms.Timer sessionTimer = new() { Interval = 1000 };
    private readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    private GameConfigurationForm? configuration;
    private CloseToTrayForm? closePrompt;
    private bool busy, armedHere, gameSeen, exitRequested, closeAfterPreparation;
    private string gameRoot, lastDetail = "", latestRuntime = "";
    private string? preparedRoot, pendingCleanupRoot;
    private DateTime detectedAtUtc;
    private LauncherStage stage = LauncherStage.Idle;
    private readonly bool smokeMode;

    internal bool IsArmed => armedHere;
    internal LauncherStage CurrentStage => stage;
    internal RoundedButton PrimaryButton => actionButton;
    internal GameCard Card => gameCard;
    internal Action<CloseToTrayForm>? ClosePromptCreated { get; set; }
    internal bool FooterRightAligned => footer.Left + actionButton.Right == cards.Left + upcoming.Right
        && footer.Left + actionGuidance.Right == cards.Left + upcoming.Right
        && actionGuidance.TextAlign == ContentAlignment.TopRight;

    internal LauncherForm(EventWaitHandle openPanel, EventWaitHandle openSettings,
        EventWaitHandle quit, string? smokeRoot = null)
    {
        this.openPanel = openPanel; this.openSettings = openSettings; this.quit = quit;
        smokeMode = smokeRoot != null;
        gameRoot = smokeRoot ?? UserPreferences.LoadGameRoot() ?? Installation.TryFindGameRoot() ?? "";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Text = "后悔药 · 游戏防遗漏提醒";
        ClientSize = new Size((int)(1120 * UiStyle.PreviewScale), (int)(740 * UiStyle.PreviewScale));
        MinimumSize = new Size((int)(790 * UiStyle.PreviewScale), (int)(620 * UiStyle.PreviewScale));
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiStyle.Canvas;
        ForeColor = UiStyle.Text;
        Font = new Font("Microsoft YaHei UI", 10f * UiStyle.PreviewScale);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        DoubleBuffered = true;

        header.BackColor = Color.FromArgb(27, 31, 38);
        header.Paint += PaintBrand;
        header.Controls.AddRange(new Control[] { brand, promise, version });
        cards.Controls.AddRange(new Control[] { gameCard, upcoming });
        upcoming.Controls.AddRange(new Control[] { upcomingMark, upcomingTitle, upcomingHint });
        footer.BackColor = UiStyle.Canvas;
        actionGuidance.TextAlign = ContentAlignment.TopRight;
        actionGuidance.AutoEllipsis = false;
        footer.Controls.AddRange(new Control[] { actionButton, actionGuidance, stopLink, progress });
        Controls.AddRange(new Control[] { header, libraryTitle, cards, footer });
        gameCard.SetGamePath(gameRoot);
        gameCard.ConfigureRequested += (_, _) => OpenConfiguration(false);
        gameCard.BrowseRequested += (_, _) => ChooseDirectory();
        actionButton.Click += async (_, _) => await PrimaryActionAsync();
        stopLink.LinkClicked += (_, _) => StopProtection();
        AcceptButton = actionButton;

        var menu = new ContextMenuStrip();
        menu.Items.Add(trayStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("打开后悔药", null, (_, _) => RestoreWindow());
        menu.Items.Add("黑神话：悟空 · 游戏配置", null, (_, _) => { RestoreWindow(); OpenConfiguration(false); });
        trayStop.Click += (_, _) => StopProtection();
        menu.Items.Add(trayStop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出后悔药", null, (_, _) => RequestExit());
        tray.Icon = Icon ?? SystemIcons.Application;
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => RestoreWindow();
        tray.Visible = !smokeMode;
        sessionTimer.Tick += SessionTick;
        sessionTimer.Start();
        FormClosing += ClosingSession;
        FormClosed += (_, _) => { sessionTimer.Dispose(); configuration?.Close(); tray.Visible = false; tray.Dispose(); tips.Dispose(); };
        SetStage(SessionControl.IsGameRunning ? LauncherStage.GameAlreadyRunning : LauncherStage.Idle);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (header == null || footer == null) return;
        int P(int n) => UiStyle.Pixels(this, n);
        int inset = P(32), gap = P(22), headerHeight = P(132), footerHeight = P(132);
        header.SetBounds(0, 0, ClientSize.Width, headerHeight);
        brand.SetBounds(P(112), P(33), P(400), P(52));
        promise.SetBounds(P(114), P(94), ClientSize.Width - P(148), P(32));
        version.SetBounds(ClientSize.Width - P(192), P(44), P(160), P(25));
        libraryTitle.SetBounds(inset, headerHeight + P(25), ClientSize.Width - inset * 2, P(30));
        footer.SetBounds(inset, ClientSize.Height - footerHeight, ClientSize.Width - inset * 2, footerHeight - P(10));
        cards.SetBounds(inset, headerHeight + P(66), ClientSize.Width - inset * 2,
            Math.Max(P(160), footer.Top - headerHeight - P(82)));
        int available = cards.ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
        bool wide = available >= P(910);
        int gameWidth = wide ? available - P(230) - gap : available;
        bool compact = !wide || cards.ClientSize.Height < P(386);
        gameCard.Compact = compact;
        gameCard.SetBounds(0, 0, gameWidth, P(compact ? 298 : 386));
        upcoming.SetBounds(wide ? gameWidth + gap : 0, wide ? 0 : gameCard.Bottom + gap,
            wide ? P(230) : available, wide ? gameCard.Height : P(150));
        int upInset = P(24);
        upcomingMark.SetBounds(upInset, P(28), upcoming.Width - upInset * 2, P(52));
        upcomingTitle.SetBounds(upInset, wide ? P(104) : P(82), upcoming.Width - upInset * 2, P(36));
        upcomingHint.SetBounds(upInset, wide ? P(160) : P(119), upcoming.Width - upInset * 2, P(58));
        if (!wide) { upcomingMark.Visible = false; upcomingTitle.Top = P(26); upcomingHint.Top = P(78); }
        else upcomingMark.Visible = true;
        cards.Reflow(Math.Max(gameCard.Bottom, upcoming.Bottom) + P(4));
        int actionRight = cards.Left + upcoming.Right - footer.Left;
        int actionWidth = Math.Min(P(244), footer.Width / 3);
        actionButton.SetBounds(actionRight - actionWidth, P(14), actionWidth, P(52));
        int guideWidth = Math.Min(P(555), actionRight);
        int guideLeft = actionRight - guideWidth;
        actionGuidance.SetBounds(guideLeft, P(80), guideWidth, P(42));
        stopLink.SetBounds(0, P(18), P(105), P(27));
        progress.SetBounds(guideLeft, P(3), guideWidth, P(4));
    }

    private void PaintBrand(object? sender, PaintEventArgs e)
    {
        float s = UiStyle.ScaleFactor(this);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(UiStyle.Accent, 5 * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(pen, new RectangleF(35 * s, 43 * s, 54 * s, 54 * s), 43, 274);
        using var brush = new SolidBrush(UiStyle.Accent);
        e.Graphics.FillEllipse(brush, 78 * s, 46 * s, 10 * s, 10 * s);
    }

    protected override void OnShown(EventArgs e)
    {
        var working = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(MinimumSize.Width, working.Width - 32), Math.Min(MinimumSize.Height, working.Height - 32));
        Size = new Size(Math.Min(Width, working.Width - 32), Math.Min(Height, working.Height - 32));
        base.OnShown(e);
    }

    private void ChooseDirectory()
    {
        if (busy || armedHere) return;
        using var dialog = new FolderBrowserDialog { Description = "选择包含游戏的 BlackMythWukong 文件夹", UseDescriptionForTitle = true };
        if (Directory.Exists(gameRoot)) dialog.SelectedPath = gameRoot;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!Installation.IsGameRoot(dialog.SelectedPath))
        {
            SetStage(LauncherStage.Failed, "目录不正确：请选择包含 b1\\Binaries\\Win64 的 BlackMythWukong 文件夹。");
            return;
        }
        gameRoot = dialog.SelectedPath;
        gameCard.SetGamePath(gameRoot);
        try { UserPreferences.SaveGameRoot(gameRoot); }
        catch (Exception ex) { Installation.Log(ex.ToString()); }
        SetStage(SessionControl.IsGameRunning ? LauncherStage.GameAlreadyRunning : LauncherStage.Idle);
    }

    private async Task PrimaryActionAsync()
    {
        if (busy) return;
        if (stage == LauncherStage.RuntimeIssue) { OpenConfiguration(true); return; }
        if (armedHere) { StopProtection(); return; }
        await PrepareAndArmAsync();
    }

    private async Task PrepareAndArmAsync()
    {
        if (busy || armedHere) return;
        if (SessionControl.IsGameRunning) { SetStage(LauncherStage.GameAlreadyRunning); return; }
        if (gameRoot.Length == 0)
        {
            gameRoot = Installation.TryFindGameRoot() ?? "";
            gameCard.SetGamePath(gameRoot);
        }
        if (!Installation.IsGameRoot(gameRoot))
        {
            SetStage(LauncherStage.Failed, "没有找到游戏：请先用卡片上的「选择目录」指定 BlackMythWukong 文件夹。");
            return;
        }
        var root = gameRoot;
        busy = true;
        SetStage(LauncherStage.Preparing);
        try
        {
            if (await Task.Run(() => Installation.NeedsPreparation(root))) await PrepareWithElevationAsync(root);
            if (!SessionControl.CanWrite(root)) await RepairAccessWithElevationAsync(root);
            try { SessionControl.Disable(root); SessionControl.Enable(root); }
            catch (UnauthorizedAccessException)
            {
                await RepairAccessWithElevationAsync(root);
                SessionControl.Disable(root); SessionControl.Enable(root);
            }
            preparedRoot = root;
            pendingCleanupRoot = null;
            armedHere = true;
            gameSeen = false;
            latestRuntime = "";
            SetStage(LauncherStage.Ready);
        }
        catch (Exception ex)
        {
            Installation.Log(ex.ToString());
            SetStage(LauncherStage.Failed, ex.Message);
        }
        finally
        {
            busy = false;
            ApplyStage();
            if (closeAfterPreparation && IsHandleCreated) BeginInvoke(new Action(RequestExit));
        }
    }

    private void StopProtection()
    {
        if (busy || !armedHere || preparedRoot == null) return;
        try
        {
            SessionControl.Disable(preparedRoot);
            if (SessionControl.IsGameRunning) pendingCleanupRoot = preparedRoot;
            armedHere = false; gameSeen = false;
            latestRuntime = "";
            SetStage(SessionControl.IsGameRunning ? LauncherStage.GameAlreadyRunning : LauncherStage.Idle,
                SessionControl.IsGameRunning ? "保护已停止。本局不会继续提醒；下次启用请先退出游戏。"
                    : "本次保护已停止。需要时再次点击「启动保护」。");
        }
        catch (Exception ex) { Installation.Log(ex.ToString()); SetStage(LauncherStage.RuntimeIssue, "停止失败：" + ex.Message); }
    }

    private void SessionTick(object? sender, EventArgs e)
    {
        if (openPanel.WaitOne(0)) RestoreWindow();
        if (openSettings.WaitOne(0)) { RestoreWindow(); OpenConfiguration(false); }
        if (quit.WaitOne(0)) { RequestExit(); return; }
        if (busy) return;
        try
        {
            bool running = SessionControl.IsGameRunning;
            if (!running && pendingCleanupRoot != null)
            {
                SessionControl.CleanupDisabled(pendingCleanupRoot);
                pendingCleanupRoot = null;
            }
            if (!armedHere)
            {
                if (stage == LauncherStage.GameAlreadyRunning && !running) SetStage(LauncherStage.Idle);
                else if (running && stage is LauncherStage.Idle or LauncherStage.GameEnded) SetStage(LauncherStage.GameAlreadyRunning);
                return;
            }
            if (!File.Exists(SessionControl.SessionFile))
            {
                StopProtection();
                return;
            }
            SessionControl.Heartbeat();
            if (running)
            {
                if (!gameSeen) { gameSeen = true; detectedAtUtc = DateTime.UtcNow; }
                var runtime = ReadFreshRuntimeStatus();
                latestRuntime = runtime ?? "尚未收到有效的实时数据。";
                SetStage(runtime?.StartsWith("实时读取正常", StringComparison.Ordinal) == true
                    ? LauncherStage.Running : runtime != null || DateTime.UtcNow - detectedAtUtc > TimeSpan.FromSeconds(45)
                        ? LauncherStage.RuntimeIssue : LauncherStage.Connecting);
            }
            else if (gameSeen)
            {
                SessionControl.Disable(preparedRoot!);
                armedHere = false; gameSeen = false;
                SetStage(LauncherStage.GameEnded);
            }
        }
        catch (Exception ex) { Installation.Log(ex.ToString()); SetStage(armedHere ? LauncherStage.RuntimeIssue : LauncherStage.Failed, ex.Message); }
    }

    private void SetStage(LauncherStage value, string? detail = null)
    {
        stage = value;
        lastDetail = detail ?? "";
        ApplyStage();
    }

    private void ApplyStage()
    {
        var view = StagePresentation.For(stage);
        actionButton.Text = view.Button;
        actionButton.Enabled = view.Enabled && !busy;
        actionButton.Primary = view.Primary;
        actionGuidance.Text = view.Guidance;
        gameCard.SetStatus(view, lastDetail.Length == 0 ? null : Shorten(lastDetail, 90));
        gameCard.DirectoryEditable = !armedHere && !busy;
        progress.Visible = stage == LauncherStage.Preparing;
        stopLink.Visible = armedHere && stage is LauncherStage.Connecting or LauncherStage.RuntimeIssue;
        trayStatus.Text = "黑神话：悟空 · " + view.Heading;
        trayStop.Enabled = armedHere && !busy;
        tray.Text = "后悔药 · " + view.Heading;
        tips.SetToolTip(gameCard, lastDetail);
    }

    private static string Shorten(string value, int max) => value.Length > max ? value[..max] + "…" : value;

    private void RestoreWindow()
    {
        Show(); WindowState = FormWindowState.Normal; Activate();
    }

    private void OpenConfiguration(bool diagnostics)
    {
        if (configuration is { IsDisposed: false })
        {
            if (diagnostics) configuration.ShowDiagnostics();
            configuration.Show(); configuration.Activate(); return;
        }
        configuration = new GameConfigurationForm(StatusSummary, () => armedHere,
            SendOverlayTest, () => SendOverlayCommand("CLEAR_HISTORY"));
        configuration.Show(this);
        if (diagnostics) configuration.ShowDiagnostics();
    }

    private string StatusSummary() => StagePresentation.For(stage).Heading
        + (lastDetail.Length > 0 ? "\n" + lastDetail : "\n" + StagePresentation.For(stage).Guidance)
        + (latestRuntime.Length > 0 ? "\n\n" + latestRuntime : "");

    internal void RequestExit()
    {
        exitRequested = true;
        if (closePrompt is { IsDisposed: false })
        {
            closePrompt.Close();
            BeginInvoke(new Action(RequestExit));
            return;
        }
        if (busy) { closeAfterPreparation = true; return; }
        Close();
    }

    private void ClosingSession(object? sender, FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            if (closePrompt != null) return;
            if (!UserPreferences.LoadSkipCloseToTrayPrompt())
            {
                using var prompt = new CloseToTrayForm();
                closePrompt = prompt;
                try
                {
                    ClosePromptCreated?.Invoke(prompt);
                    if (prompt.ShowDialog(this) != DialogResult.OK) return;
                }
                finally { closePrompt = null; }
            }
            configuration?.Hide();
            Hide();
            return;
        }
        if (busy) { closeAfterPreparation = true; e.Cancel = true; return; }
        if (preparedRoot == null) return;
        try
        {
            bool running = SessionControl.IsGameRunning;
            SessionControl.Disable(preparedRoot);
            if (running) StartCleanupAfterGame(preparedRoot);
        }
        catch (Exception ex) { Installation.Log(ex.ToString()); }
    }

    internal void PreviewStage(LauncherStage value) => SetStage(value);

    private static async Task PrepareWithElevationAsync(string root)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("无法识别当前 Windows 用户。");
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        { await Installation.InstallAsync(root, _ => { }, sid); return; }
        await RunElevatedAsync("--install", "--game-root", root, "--sid", sid);
    }

    private static async Task RepairAccessWithElevationAsync(string root)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("无法识别当前 Windows 用户。");
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        { Installation.RepairSessionAccess(root, sid); return; }
        await RunElevatedAsync("--grant-session-access", "--game-root", root, "--sid", sid);
        if (!SessionControl.CanWrite(root)) throw new UnauthorizedAccessException("组件目录仍不可写，请打开游戏配置中的诊断。");
    }

    private static async Task RunElevatedAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("无法找到当前 EXE。"))
            { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("管理员准备进程未启动。");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new InvalidOperationException("组件准备失败，请打开游戏配置中的诊断查看日志。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { throw new InvalidOperationException("系统授权被取消。需要时点击「重试启动」。", ex); }
    }

    private static void StartCleanupAfterGame(string root)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("无法启动清理程序。"))
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("--wait-clean"); start.ArgumentList.Add("--game-root"); start.ArgumentList.Add(root);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动清理程序。");
    }

    private static string? ReadFreshRuntimeStatus()
    {
        try
        {
            var file = Path.Combine(Path.GetDirectoryName(SessionControl.SessionFile)!, "runtime-status.txt");
            var content = File.ReadAllText(file);
            int separator = content.IndexOf('|');
            if (separator < 1 || !long.TryParse(content[..separator], out var ticks)) return null;
            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            return age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(8) ? content[(separator + 1)..] : null;
        }
        catch (Exception) { return null; }
    }

    private static void SendOverlayTest()
    {
        var levels = new[] { "显示测试：这不是当前位置的遗漏提醒。", "一级提示：已进入可操作的详情窗口。",
            "二级提示：键盘与手柄都可以逐级展开。", "三级提示：测试完成，可以关闭详情窗口。" };
        SendOverlayCommand(string.Join("|", new[] { "SHOW", Encode("overlay_demo") }.Concat(levels.Select(Encode))));
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static void SendOverlayCommand(string command)
    {
        using var pipe = new NamedPipeClientStream(".", "WukongGuardOverlay", PipeDirection.InOut);
        try { pipe.Connect(500); }
        catch (TimeoutException) { throw new InvalidOperationException("游戏内提醒窗口尚未连接，请先启用保护并启动游戏。"); }
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.WriteLine(command); writer.Flush();
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        // Never block the UI indefinitely if the overlay disconnects.
        var reply = reader.ReadLineAsync();
        if (!reply.Wait(1500) || reply.Result != "ACK") throw new IOException("游戏内提醒窗口没有确认操作，请查看日志。");
    }
}
