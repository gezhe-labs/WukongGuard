using RegretPill.Shared;

namespace WukongGuard.Installer;

internal sealed class HistoryDetailForm : Form
{
    private readonly HistoryStore.Entry entry;
    private readonly Label heading = UiStyle.Label("按需逐级揭示", 18, true);
    private readonly TextBox text = new() { ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None,
        ScrollBars = ScrollBars.Vertical, BackColor = UiStyle.Canvas, ForeColor = UiStyle.Text };
    private readonly Label levelText = UiStyle.Label("提示级别 0", 9, false, UiStyle.Muted);
    private readonly RoundedButton next = UiStyle.Button("更多提示", true), close = UiStyle.Button("关闭");
    private int level;

    internal HistoryDetailForm(HistoryStore.Entry entry)
    {
        this.entry = entry;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Text = "后悔药 · 黑神话：悟空提醒详情";
        ClientSize = new Size(650, 360);
        MinimumSize = new Size(570, 320);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;
        ForeColor = UiStyle.Text;
        Font = new Font("Microsoft YaHei UI", 11);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        text.Text = entry.Levels[0];
        next.Click += (_, _) =>
        {
            if (level >= 3) return;
            level++;
            text.Text = this.entry.Levels[level];
            levelText.Text = "提示级别 " + level;
            next.Enabled = level < 3;
            if (level == 3) next.Text = "已全部展开";
        };
        close.Click += (_, _) => Close();
        CancelButton = close;
        Controls.AddRange(new Control[] { heading, text, levelText, next, close });
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (heading == null || close == null) return;
        int P(int n) => UiStyle.Pixels(this, n);
        heading.SetBounds(P(26), P(24), ClientSize.Width - P(52), P(40));
        text.SetBounds(P(26), P(90), ClientSize.Width - P(52), Math.Max(P(80), ClientSize.Height - P(190)));
        levelText.SetBounds(P(26), ClientSize.Height - P(69), P(240), P(25));
        next.SetBounds(ClientSize.Width - P(264), ClientSize.Height - P(73), P(132), P(43));
        close.SetBounds(ClientSize.Width - P(118), ClientSize.Height - P(73), P(92), P(43));
    }
}
