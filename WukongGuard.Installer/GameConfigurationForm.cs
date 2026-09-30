using System.Diagnostics;
using System.IO.Compression;
using RegretPill.Shared;

namespace WukongGuard.Installer;

internal sealed class GameConfigurationForm : Form
{
    private readonly Func<string> statusSummary;
    private readonly Func<bool> isArmed;
    private readonly Action showTest, clearOverlayHistory;
    private readonly Label title = UiStyle.Label("黑神话：悟空", 21, true);
    private readonly Label subtitle = UiStyle.Label("此处的提醒、输入与记录仅适用于这款游戏。", 10, false, UiStyle.Muted);
    private readonly RoundedButton[] tabs = { UiStyle.Button("提醒设置"), UiStyle.Button("提醒记录"), UiStyle.Button("诊断") };
    private readonly Panel[] pages = { new SurfacePanel(), new SurfacePanel(), new SurfacePanel() };
    private readonly ListView history = new() { View = View.Details, FullRowSelect = true, MultiSelect = false,
        HideSelection = false, BorderStyle = BorderStyle.None, BackColor = UiStyle.Surface, ForeColor = UiStyle.Text };
    private readonly Label emptyHistory = UiStyle.Label("暂时没有提醒记录\n收到的游戏提醒会出现在这里。", 11, false, UiStyle.Muted);
    private readonly Label diagnosticStatus = UiStyle.Label("", 11);
    private readonly Label feedback = UiStyle.Label("", 9, false, UiStyle.Muted);
    private readonly Label saved = UiStyle.Label("设置自动保存，对本机后续提醒生效。", 9, false, UiStyle.Muted);
    private readonly RoundedButton clear = UiStyle.Button("清空记录");
    private readonly RoundedButton test = UiStyle.Button("显示测试提醒", true);
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 1000 };

    internal GameConfigurationForm(Func<string> statusSummary, Func<bool> isArmed,
        Action showTest, Action clearOverlayHistory)
    {
        this.statusSummary = statusSummary; this.isArmed = isArmed;
        this.showTest = showTest; this.clearOverlayHistory = clearOverlayHistory;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Text = "后悔药 · 黑神话：悟空配置";
        ClientSize = new Size((int)(820 * UiStyle.PreviewScale), (int)(650 * UiStyle.PreviewScale));
        MinimumSize = new Size((int)(740 * UiStyle.PreviewScale), (int)(610 * UiStyle.PreviewScale));
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;
        ForeColor = UiStyle.Text;
        Font = new Font("Microsoft YaHei UI", 10f * UiStyle.PreviewScale);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        DoubleBuffered = true;
        Controls.AddRange(new Control[] { title, subtitle });
        for (int i = 0; i < tabs.Length; i++)
        {
            int index = i;
            tabs[i].Click += (_, _) => ShowPage(index);
            Controls.Add(tabs[i]); Controls.Add(pages[i]);
            pages[i].AutoScroll = true;
        }
        BuildSettings(); BuildHistory(); BuildDiagnostics(); ShowPage(0);
        updateTimer.Tick += (_, _) => RefreshDiagnostics();
        updateTimer.Start();
        FormClosed += (_, _) => updateTimer.Dispose();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (tabs == null || pages == null) return;
        int P(int n) => UiStyle.Pixels(this, n);
        title.SetBounds(P(28), P(24), ClientSize.Width - P(56), P(40));
        subtitle.SetBounds(P(29), P(76), ClientSize.Width - P(58), P(29));
        for (int i = 0; i < tabs.Length; i++)
        {
            tabs[i].SetBounds(P(28 + i * 142), P(119), P(130), P(40));
            pages[i].SetBounds(P(28), P(178), ClientSize.Width - P(56), ClientSize.Height - P(202));
        }
    }

    private void BuildSettings()
    {
        var page = pages[0];
        var required = UiStyle.Label("不可补救内容提醒", 13, true);
        var requiredHint = UiStyle.Label("保护启动后默认开启。在可能错过内容的节点给出提示，详情由你主动展开。", 10, false, UiStyle.Muted);
        var hidden = new CheckBox { Text = "额外开启隐藏地区提示", Checked = UserPreferences.LoadHiddenAreaHints(),
            ForeColor = UiStyle.Text, BackColor = UiStyle.Surface, AutoSize = false };
        var hiddenHint = UiStyle.Label("开启后，也会提醒可回访的隐藏地区；默认关闭。", 9, false, UiStyle.Muted);
        var keyTitle = UiStyle.Label("查看更多的键盘快捷键", 11, true);
        var hotkey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = UiStyle.Raised,
            ForeColor = UiStyle.Text, FlatStyle = FlatStyle.Flat, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24 };
        hotkey.DrawItem += (_, e) =>
        {
            using var brush = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? UiStyle.Border : UiStyle.Raised);
            e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, hotkey.Items[e.Index]?.ToString(), hotkey.Font,
                Rectangle.Inflate(e.Bounds, -8, 0), UiStyle.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        hotkey.Items.AddRange(new object[] { "Ctrl+Shift+G", "Ctrl+Alt+Shift+G" });
        hotkey.SelectedIndex = UserPreferences.LoadMoreHotkey() == "CtrlAltShiftG" ? 1 : 0;
        var gamepad = new CheckBox { Text = "Xbox 手柄长按 Menu 约 1 秒打开详情", Checked = UserPreferences.LoadGamepadMenuHold(),
            ForeColor = UiStyle.Text, BackColor = UiStyle.Surface, AutoSize = false };
        var inputHint = UiStyle.Label("普通提醒不获取焦点，10 秒后自动收起。\n主动查看更多后才进入详情；手柄 Menu 可能同时打开游戏菜单。", 9, false, UiStyle.Muted);
        hidden.CheckedChanged += (_, _) => Save(() => UserPreferences.SaveHiddenAreaHints(hidden.Checked));
        hotkey.SelectedIndexChanged += (_, _) => Save(() => UserPreferences.SaveMoreHotkey(hotkey.SelectedIndex == 1 ? "CtrlAltShiftG" : "CtrlShiftG"));
        gamepad.CheckedChanged += (_, _) => Save(() => UserPreferences.SaveGamepadMenuHold(gamepad.Checked));
        page.Controls.AddRange(new Control[] { required, requiredHint, hidden, hiddenHint, keyTitle, hotkey, gamepad, inputHint, saved });
        page.Layout += (_, _) =>
        {
            int P(int n) => UiStyle.Pixels(this, n);
            int width = page.ClientSize.Width - P(48);
            required.SetBounds(P(24), P(22), width, P(31));
            requiredHint.SetBounds(P(24), P(61), width, P(44));
            hidden.SetBounds(P(24), P(119), width, P(30));
            hiddenHint.SetBounds(P(46), P(155), width - P(22), P(28));
            keyTitle.SetBounds(P(24), P(200), width, P(29));
            hotkey.SetBounds(P(24), P(237), Math.Min(P(340), width), P(32));
            gamepad.SetBounds(P(24), P(295), width, P(32));
            inputHint.SetBounds(P(24), P(339), width, P(52));
            saved.SetBounds(P(24), P(408), width, P(31));
            page.AutoScrollMinSize = new Size(0, P(441));
        };
    }

    private void BuildHistory()
    {
        var page = pages[1];
        var heading = UiStyle.Label("最近收到的提醒", 13, true);
        var help = UiStyle.Label("双击记录或点击「查看详情」，按需逐级展开提示。", 10, false, UiStyle.Muted);
        var detail = UiStyle.Button("查看详情", true);
        var refresh = UiStyle.Button("刷新");
        history.Columns.Add("时间", 130); history.Columns.Add("提醒", 530);
        history.OwnerDraw = true;
        history.DrawColumnHeader += (_, e) =>
        {
            if (e.Header == null) return;
            using var brush = new SolidBrush(UiStyle.Raised);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, history.Font, Rectangle.Inflate(e.Bounds, -6, 0),
                UiStyle.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        };
        history.DrawItem += (_, _) => { };
        history.DrawSubItem += (_, e) =>
        {
            if (e.Item == null || e.SubItem == null) return;
            using var brush = new SolidBrush(e.Item.Selected ? UiStyle.Raised : UiStyle.Surface);
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, history.Font, Rectangle.Inflate(e.Bounds, -6, 0),
                UiStyle.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        history.DoubleClick += (_, _) => OpenHistoryDetail();
        detail.Click += (_, _) => OpenHistoryDetail();
        refresh.Click += (_, _) => RefreshHistory();
        clear.Click += (_, _) =>
        {
            try { HistoryStore.Clear(); RefreshHistory(); }
            catch (Exception ex) { ShowFeedback("清空失败：" + ex.Message, true); return; }
            try { clearOverlayHistory(); }
            catch (IOException) { }
            catch (InvalidOperationException) { }
        };
        page.Controls.AddRange(new Control[] { heading, help, history, emptyHistory, detail, refresh, clear });
        page.Layout += (_, _) =>
        {
            int P(int n) => UiStyle.Pixels(this, n);
            int width = page.ClientSize.Width - P(48);
            heading.SetBounds(P(24), P(22), width, P(31));
            help.SetBounds(P(24), P(62), width, P(30));
            history.SetBounds(P(24), P(110), width, Math.Max(P(150), page.ClientSize.Height - P(195)));
            history.Columns[0].Width = P(138);
            history.Columns[1].Width = Math.Max(P(150), width - P(155));
            emptyHistory.SetBounds(P(40), P(160), width - P(32), P(80));
            int top = history.Bottom + P(24);
            detail.SetBounds(P(24), top, P(124), P(40));
            refresh.SetBounds(P(160), top, P(88), P(40));
            clear.SetBounds(page.ClientSize.Width - P(148), top, P(124), P(40));
            page.AutoScrollMinSize = new Size(0, top + P(58));
        };
    }

    private void BuildDiagnostics()
    {
        var page = pages[2];
        var heading = UiStyle.Label("游戏连接状态", 13, true);
        var help = UiStyle.Label("测试提醒只检查显示和交互，不代表当前位置有遗漏。", 10, false, UiStyle.Muted);
        var logs = UiStyle.Button("打开日志目录");
        var export = UiStyle.Button("导出诊断");
        var notice = UiStyle.Label("验证版提示：目前配置了 7 条不可补救提醒、6 条可选隐藏地区提示。\n规则主要根据地图与坐标判断，尚不能可靠确认每项内容是否已经完成。", 9, false, UiStyle.Muted);
        test.Click += (_, _) =>
        {
            try { showTest(); ShowFeedback("测试提醒已发送，请切回游戏查看。", false); }
            catch (Exception ex) { ShowFeedback(ex.Message, true); }
        };
        logs.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { LogDirectory } });
            }
            catch (Exception ex) { ShowFeedback("无法打开日志：" + ex.Message, true); }
        };
        export.Click += (_, _) => ExportDiagnostics();
        page.Controls.AddRange(new Control[] { heading, diagnosticStatus, help, test, logs, export, feedback, notice });
        page.Layout += (_, _) =>
        {
            int P(int n) => UiStyle.Pixels(this, n);
            int width = page.ClientSize.Width - P(48);
            heading.SetBounds(P(24), P(22), width, P(31));
            diagnosticStatus.SetBounds(P(24), P(67), width, P(120));
            help.SetBounds(P(24), P(202), width, P(32));
            test.SetBounds(P(24), P(250), P(154), P(42));
            logs.SetBounds(P(190), P(250), P(148), P(42));
            export.SetBounds(P(350), P(250), P(126), P(42));
            feedback.SetBounds(P(24), P(309), width, P(50));
            notice.SetBounds(P(24), P(382), width, P(62));
            page.AutoScrollMinSize = new Size(0, P(446));
        };
        RefreshDiagnostics();
    }

    private void Save(Action action)
    {
        try { action(); saved.Text = "已保存，对本机后续提醒生效。"; saved.ForeColor = UiStyle.Success; }
        catch (Exception ex) { saved.Text = "保存失败：" + ex.Message; saved.ForeColor = UiStyle.Error; }
    }

    private void RefreshHistory()
    {
        history.Items.Clear();
        try
        {
            foreach (var entry in HistoryStore.Load())
            {
                var item = new ListViewItem(entry.SeenAt.ToString("MM-dd HH:mm")) { Tag = entry };
                item.SubItems.Add(entry.Levels[0]); history.Items.Add(item);
            }
            emptyHistory.Visible = history.Items.Count == 0;
            clear.Enabled = history.Items.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { emptyHistory.Text = "提醒记录暂时无法读取，请稍后刷新。"; emptyHistory.Visible = true; }
    }

    private void OpenHistoryDetail()
    {
        if (history.SelectedItems.Count == 1 && history.SelectedItems[0].Tag is HistoryStore.Entry entry)
            new HistoryDetailForm(entry).ShowDialog(this);
    }

    private void RefreshDiagnostics()
    {
        if (diagnosticStatus == null || statusSummary == null) return;
        diagnosticStatus.Text = statusSummary();
        test.Enabled = isArmed();
    }

    private void ShowFeedback(string message, bool error)
    { feedback.Text = message; feedback.ForeColor = error ? UiStyle.Error : UiStyle.Success; }

    private static string LogDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WukongGuard");

    private void ExportDiagnostics()
    {
        using var dialog = new SaveFileDialog { Title = "导出黑神话：悟空诊断", Filter = "诊断压缩包 (*.zip)|*.zip",
            FileName = "后悔药-诊断-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using var destination = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write);
            using var archive = new ZipArchive(destination, ZipArchiveMode.Create);
            foreach (var name in new[] { "guard.log", "overlay.log", "overlay.log.previous", "installer.log", "runtime-status.txt", "settings.json" })
            {
                var file = Path.Combine(LogDirectory, name);
                if (File.Exists(file)) archive.CreateEntryFromFile(file, name);
            }
            var status = archive.CreateEntry("connection.txt");
            using var writer = new StreamWriter(status.Open());
            writer.WriteLine("后悔药 " + Installation.ProductVersion); writer.WriteLine(statusSummary());
            ShowFeedback("诊断已导出。", false);
        }
        catch (Exception ex) { ShowFeedback("导出失败：" + ex.Message, true); }
    }

    internal void ShowDiagnostics() => ShowPage(2);
    internal void PreviewPage(int index) => ShowPage(index);
    private void ShowPage(int index)
    {
        for (int i = 0; i < pages.Length; i++) { pages[i].Visible = i == index; tabs[i].Primary = i == index; }
        if (index == 1) RefreshHistory();
        if (index == 2) RefreshDiagnostics();
    }
}
