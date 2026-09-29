using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using RegretPill.Shared;

namespace WukongGuard.Installer;

internal sealed class LauncherForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(22, 25, 31);
    private static readonly Color Surface = Color.FromArgb(33, 38, 45);
    private static readonly Color Muted = Color.FromArgb(169, 177, 186);
    private static readonly Color Accent = Color.FromArgb(249, 194, 99);
    private readonly EventWaitHandle openPanel;
    private readonly EventWaitHandle openSettings;
    private readonly EventWaitHandle quit;
    private readonly TextBox gamePath = new();
    private readonly Label status = new();
    private readonly Label guidance = new();
    private readonly Button retry = new();
    private readonly ProgressBar progress = new();
    private readonly ListBox history = new();
    private readonly Panel[] pages = new Panel[4];
    private readonly System.Windows.Forms.Timer sessionTimer = new() { Interval = 1000 };
    private bool busy;
    private bool armedHere;
    private bool gameSeen;
    private bool closeAfterPreparation;
    private string? preparedRoot;

    internal LauncherForm(EventWaitHandle openPanel, EventWaitHandle openSettings,
        EventWaitHandle quit)
    {
        this.openPanel = openPanel;
        this.openSettings = openSettings;
        this.quit = quit;
        Text = "后悔药 · 游戏防遗漏提醒";
        ClientSize = new Size(760, 510);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Canvas;
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10f);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var header = new Panel { Bounds = new Rectangle(0, 0, 760, 110), BackColor = Surface };
        header.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(Accent, 5);
            e.Graphics.DrawArc(pen, new Rectangle(28, 25, 49, 49), 43, 274);
            using var brush = new SolidBrush(Accent);
            e.Graphics.FillEllipse(brush, 68, 31, 9, 9);
        };
        header.Controls.Add(Label("后悔药", 96, 18, 290, 39, 21, true, Color.White));
        header.Controls.Add(Label("错过之前，提醒一下", 97, 61, 330, 28, 10, false, Muted));
        Controls.Add(header);

        string[] tabs = { "首页", "提醒记录", "设置", "诊断" };
        for (int i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var nav = Button(tabs[i], 24 + i * 126, 118, 115, 37, false);
            nav.Click += (_, _) => ShowPage(index);
            Controls.Add(nav);
            pages[i] = new Panel { Bounds = new Rectangle(24, 170, 712, 309), BackColor = Surface };
            Controls.Add(pages[i]);
        }
        BuildHome();
        BuildHistory();
        BuildSettings();
        BuildDiagnostics();
        ShowPage(0);
        gamePath.Text = Installation.TryFindGameRoot() ?? "";
        sessionTimer.Tick += SessionTick;
        sessionTimer.Start();
        Shown += async (_, _) => await PrepareAndArmAsync();
        FormClosing += ClosingSession;
        FormClosed += (_, _) => sessionTimer.Dispose();
    }

    private static Label Label(string text, int x, int y, int width, int height,
        float size = 10, bool bold = false, Color? color = null) => new()
    {
        Text = text,
        Bounds = new Rectangle(x, y, width, height),
        ForeColor = color ?? Color.White,
        Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        BackColor = Color.Transparent
    };

    private static Button Button(string text, int x, int y, int width, int height, bool primary)
    {
        var button = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, width, height),
            BackColor = primary ? Accent : Color.FromArgb(46, 52, 60),
            ForeColor = primary ? Canvas : Color.White,
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
            UseVisualStyleBackColor = false
        };
        UiStyle.Round(button);
        return button;
    }

    private void BuildHome()
    {
        var page = pages[0];
        page.Controls.Add(Label("《黑神话：悟空》", 22, 20, 380, 34, 16, true));
        page.Controls.Add(Label("先打开后悔药，再由你从 Steam 启动游戏。", 22, 58, 650, 26,
            10, false, Muted));
        status.SetBounds(22, 101, 660, 30);
        status.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
        status.ForeColor = Accent;
        status.Text = "正在检查…";
        page.Controls.Add(status);
        guidance.SetBounds(22, 136, 660, 43);
        guidance.ForeColor = Muted;
        guidance.Text = "首次使用会自动准备组件；以后打开即待命。";
        page.Controls.Add(guidance);
        progress.SetBounds(22, 180, 660, 5);
        progress.Style = ProgressBarStyle.Marquee;
        progress.Visible = false;
        page.Controls.Add(progress);
        page.Controls.Add(Label("游戏目录", 22, 201, 120, 24, 9, false, Muted));
        gamePath.SetBounds(22, 230, 520, 27);
        gamePath.BackColor = Color.FromArgb(24, 28, 34);
        gamePath.ForeColor = Color.White;
        page.Controls.Add(gamePath);
        var browse = Button("选择目录", 552, 228, 130, 32, false);
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择 BlackMythWukong 游戏目录" };
            if (dialog.ShowDialog(this) == DialogResult.OK) gamePath.Text = dialog.SelectedPath;
        };
        page.Controls.Add(browse);
        retry.Text = "检查并待命";
        retry.Bounds = new Rectangle(522, 267, 160, 33);
        retry.FlatStyle = FlatStyle.Flat;
        retry.BackColor = Accent;
        retry.ForeColor = Canvas;
        UiStyle.Round(retry);
        retry.Click += async (_, _) => await PrepareAndArmAsync();
        page.Controls.Add(retry);
    }

    private void BuildHistory()
    {
        var page = pages[1];
        page.Controls.Add(Label("最近提醒", 22, 18, 300, 34, 16, true));
        page.Controls.Add(Label("只记录本机收到的提醒；双击可查看逐级提示。", 22, 55,
            660, 26, 10, false, Muted));
        history.SetBounds(22, 92, 660, 167);
        history.BackColor = Color.FromArgb(24, 28, 34);
        history.ForeColor = Color.White;
        history.BorderStyle = BorderStyle.None;
        history.DoubleClick += (_, _) =>
        {
            if (history.SelectedItem is HistoryStore.Entry entry)
                new HistoryDetailForm(entry).ShowDialog(this);
        };
        page.Controls.Add(history);
        var refresh = Button("刷新", 22, 266, 90, 32, false);
        refresh.Click += (_, _) => RefreshHistory();
        page.Controls.Add(refresh);
        var clear = Button("清空记录", 122, 266, 125, 32, false);
        clear.Click += (_, _) =>
        {
            try { HistoryStore.Clear(); RefreshHistory(); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法清空提醒记录：" + ex.Message, "后悔药");
                return;
            }
            try { SendOverlayCommand("CLEAR_HISTORY"); }
            catch (IOException) { }
            catch (TimeoutException) { }
        };
        page.Controls.Add(clear);
    }

    private void BuildSettings()
    {
        var page = pages[2];
        page.Controls.Add(Label("提醒设置", 22, 18, 300, 34, 16, true));
        var hidden = new CheckBox
        {
            Text = "额外显示隐藏地区提示（默认关闭）",
            Bounds = new Rectangle(22, 63, 620, 33),
            Checked = UserPreferences.LoadHiddenAreaHints(),
            ForeColor = Color.White
        };
        hidden.CheckedChanged += (_, _) => SaveSetting(() => UserPreferences.SaveHiddenAreaHints(hidden.Checked));
        page.Controls.Add(hidden);
        page.Controls.Add(Label("查看更多的键盘快捷键", 22, 108, 340, 27));
        var hotkey = new ComboBox
        {
            Bounds = new Rectangle(22, 140, 320, 31),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        hotkey.Items.AddRange(new object[] { "Ctrl+Shift+G", "Ctrl+Alt+Shift+G" });
        hotkey.SelectedIndex = UserPreferences.LoadMoreHotkey() == "CtrlAltShiftG" ? 1 : 0;
        hotkey.SelectedIndexChanged += (_, _) => SaveSetting(() =>
            UserPreferences.SaveMoreHotkey(hotkey.SelectedIndex == 1 ? "CtrlAltShiftG" : "CtrlShiftG"));
        page.Controls.Add(hotkey);
        var gamepad = new CheckBox
        {
            Text = "手柄长按 Menu 约 1 秒打开详情",
            Bounds = new Rectangle(22, 190, 620, 33),
            Checked = UserPreferences.LoadGamepadMenuHold(),
            ForeColor = Color.White
        };
        gamepad.CheckedChanged += (_, _) => SaveSetting(() => UserPreferences.SaveGamepadMenuHold(gamepad.Checked));
        page.Controls.Add(gamepad);
        page.Controls.Add(Label("普通提醒不抢焦点，10 秒后自动收起；详情由你主动打开。",
            22, 251, 660, 42, 9, false, Muted));
    }

    private void BuildDiagnostics()
    {
        var page = pages[3];
        page.Controls.Add(Label("诊断", 22, 18, 300, 34, 16, true));
        page.Controls.Add(Label("测试提醒仅检查显示与输入，不表示附近有遗漏。",
            22, 60, 660, 30, 10, false, Muted));
        var demo = Button("显示测试提醒", 22, 111, 165, 38, true);
        demo.Click += (_, _) =>
        {
            try { SendOverlayTest(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "后悔药 · 诊断"); }
        };
        page.Controls.Add(demo);
        var logs = Button("打开日志目录", 200, 111, 165, 38, false);
        logs.Click += (_, _) =>
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WukongGuard");
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true,
                ArgumentList = { path } });
        };
        page.Controls.Add(logs);
        page.Controls.Add(Label("连接状态以游戏运行时日志为准。无法触发时，请导出日志和到达地点。",
            22, 184, 650, 50, 9, false, Muted));
    }

    private void ShowPage(int index)
    {
        for (int i = 0; i < pages.Length; i++) pages[i].Visible = i == index;
        if (index == 1) RefreshHistory();
    }

    private void RefreshHistory()
    {
        history.Items.Clear();
        foreach (var entry in HistoryStore.Load()) history.Items.Add(entry);
    }

    private void SaveSetting(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, "无法保存设置：" + ex.Message, "后悔药"); }
    }

    private async Task PrepareAndArmAsync()
    {
        if (busy || armedHere) return;
        var root = gamePath.Text.Trim();
        if (root.Length == 0)
        {
            SetStatus("未找到游戏", "请选择 BlackMythWukong 游戏目录，然后点击“检查并待命”。");
            return;
        }
        if (!Installation.IsGameRoot(root))
        {
            SetStatus("游戏目录不正确", "请选择包含 b1\\Binaries\\Win64 的 BlackMythWukong 文件夹。");
            return;
        }
        if (SessionControl.IsGameRunning)
        {
            SetStatus("游戏已经运行", "请先退出游戏，再打开后悔药；本次游戏无法补加载运行时组件。");
            return;
        }
        busy = true;
        retry.Enabled = false;
        gamePath.Enabled = false;
        progress.Visible = true;
        try
        {
            if (Installation.NeedsPreparation(root))
            {
                SetStatus("首次准备或更新中…", "正在校验文件并配置游戏组件。可能需要一次管理员授权。");
                await PrepareWithElevationAsync(root);
            }
            if (!SessionControl.CanWrite(root))
            {
                SetStatus("修复会话权限中…", "正在恢复后悔药组件目录的写入权限。");
                await RepairAccessWithElevationAsync(root);
            }
            // A prior crash can leave an active DLL, but never reuse its old lease.
            try
            {
                SessionControl.Disable(root);
                SessionControl.Enable(root);
            }
            catch (UnauthorizedAccessException)
            {
                await RepairAccessWithElevationAsync(root);
                SessionControl.Disable(root);
                SessionControl.Enable(root);
            }
            preparedRoot = root;
            armedHere = true;
            gameSeen = false;
            SetStatus("已就绪，等待游戏启动", "现在由你从 Steam 启动《黑神话：悟空》。后悔药会自动连接。");
        }
        catch (Exception ex)
        {
            Installation.Log(ex.ToString());
            SetStatus("准备未完成", ex.Message + "  详细记录见诊断页中的日志目录。");
        }
        finally
        {
            busy = false;
            retry.Enabled = !armedHere;
            gamePath.Enabled = !armedHere;
            progress.Visible = false;
            if (closeAfterPreparation && IsHandleCreated) BeginInvoke(new Action(Close));
        }
    }

    private static async Task PrepareWithElevationAsync(string root)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User?.Value
            ?? throw new InvalidOperationException("无法识别当前 Windows 用户。");
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            await Installation.InstallAsync(root, _ => { }, userSid);
            return;
        }
        await RunElevatedAsync("--install", "--game-root", root, "--sid", userSid);
    }

    private static async Task RepairAccessWithElevationAsync(string root)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var userSid = identity.User?.Value
            ?? throw new InvalidOperationException("无法识别当前 Windows 用户。");
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Installation.RepairSessionAccess(root, userSid);
            return;
        }
        await RunElevatedAsync("--grant-session-access", "--game-root", root, "--sid", userSid);
        if (!SessionControl.CanWrite(root))
            throw new UnauthorizedAccessException("会话目录仍不可写，请在诊断页查看 installer.log。");
    }

    private static async Task RunElevatedAsync(params string[] arguments)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法找到当前 EXE。");
        var start = new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas" };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("管理员准备进程未启动。");
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("自动准备失败，请到诊断页打开 installer.log 查看原因。");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("未获得管理员授权；游戏目录仍未配置。", ex);
        }
    }

    private async void SessionTick(object? sender, EventArgs e)
    {
        if (openPanel.WaitOne(0))
        {
            Show(); WindowState = FormWindowState.Normal; Activate();
            if (!armedHere && !busy) await PrepareAndArmAsync();
        }
        if (openSettings.WaitOne(0))
        {
            Show(); WindowState = FormWindowState.Normal; ShowPage(2); Activate();
        }
        if (quit.WaitOne(0)) { Close(); return; }
        if (!armedHere) return;
        try
        {
            if (!File.Exists(SessionControl.SessionFile))
            {
                armedHere = false;
                SetStatus("本次提醒已停用", "游戏退出后会清理组件；可重新打开后悔药开始下一次游戏。");
                return;
            }
            SessionControl.Heartbeat();
            if (SessionControl.IsGameRunning)
            {
                gameSeen = true;
                var runtime = ReadFreshRuntimeStatus();
                SetStatus(runtime?.StartsWith("实时读取正常", StringComparison.Ordinal) == true
                    ? "实时读取正常" : runtime == null
                        ? "游戏运行中，等待状态" : "游戏运行中 · 状态需检查",
                    runtime ?? "正在等待《黑神话：悟空》的运行时状态；可在诊断页查看日志。");
            }
            else if (gameSeen)
            {
                SessionControl.Disable(preparedRoot!);
                armedHere = false;
                gameSeen = false;
                retry.Enabled = true;
                gamePath.Enabled = true;
                SetStatus("本次游戏已结束", "提醒已自动停用。下次游戏前重新打开后悔药即可。");
            }
        }
        catch (Exception ex)
        {
            Installation.Log(ex.ToString());
            SetStatus("会话检查异常", "请到诊断页查看日志，退出游戏后重新打开后悔药。");
        }
    }

    private void ClosingSession(object? sender, FormClosingEventArgs e)
    {
        if (busy)
        {
            closeAfterPreparation = true;
            SetStatus("正在完成配置", "配置完成后会自动退出并停用本次提醒。");
            e.Cancel = true;
            return;
        }
        if (preparedRoot == null) return;
        try
        {
            var gameRunning = SessionControl.IsGameRunning;
            SessionControl.Disable(preparedRoot);
            if (gameRunning) StartCleanupAfterGame(preparedRoot);
        }
        catch (Exception ex) { Installation.Log(ex.ToString()); }
    }

    private static void StartCleanupAfterGame(string root)
    {
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("无法启动会话清理程序。");
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        start.ArgumentList.Add("--wait-clean");
        start.ArgumentList.Add("--game-root");
        start.ArgumentList.Add(root);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("无法启动会话清理程序。");
    }

    private void SetStatus(string heading, string detail)
    {
        status.Text = heading;
        guidance.Text = detail;
    }

    private static string? ReadFreshRuntimeStatus()
    {
        try
        {
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WukongGuard", "runtime-status.txt");
            var content = File.ReadAllText(file);
            var separator = content.IndexOf('|');
            if (separator < 1 || !long.TryParse(content[..separator], out var ticks)) return null;
            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            return age >= TimeSpan.Zero && age < TimeSpan.FromSeconds(8)
                ? content[(separator + 1)..] : null;
        }
        catch (Exception) { return null; }
    }

    private static void SendOverlayTest()
    {
        using var pipe = new NamedPipeClientStream(".", "WukongGuardOverlay", PipeDirection.InOut);
        try { pipe.Connect(700); }
        catch (TimeoutException) { throw new InvalidOperationException("游戏内提醒窗口尚未运行。请先启动游戏。 "); }
        var levels = new[]
        {
            "显示测试：这不是当前位置的遗漏提醒。",
            "一级提示：已进入可操作的详情窗口。",
            "二级提示：键盘与手柄都可以逐级展开。",
            "三级提示：测试完成，可以关闭详情窗口。"
        };
        var fields = new[] { "SHOW", Encode("overlay_demo") }.Concat(levels.Select(Encode));
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.WriteLine(string.Join("|", fields));
        writer.Flush();
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        if (reader.ReadLine() != "ACK") throw new IOException("提醒窗口没有确认显示测试。");
    }

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static void SendOverlayCommand(string command)
    {
        using var pipe = new NamedPipeClientStream(".", "WukongGuardOverlay", PipeDirection.InOut);
        pipe.Connect(200);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.WriteLine(command);
        writer.Flush();
        using var reader = new StreamReader(pipe, Encoding.UTF8);
        if (reader.ReadLine() != "ACK") throw new IOException("提醒窗口未确认操作。");
    }
}
