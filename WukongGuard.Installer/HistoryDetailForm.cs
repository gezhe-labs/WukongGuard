using System.Drawing;
using RegretPill.Shared;

namespace WukongGuard.Installer;

internal sealed class HistoryDetailForm : Form
{
    private readonly HistoryStore.Entry entry;
    private readonly Label text = new();
    private readonly Label levelText = new();
    private readonly Button next = new();
    private int level;

    internal HistoryDetailForm(HistoryStore.Entry entry)
    {
        this.entry = entry;
        Text = "后悔药 · 提醒详情";
        ClientSize = new Size(600, 255);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = Color.FromArgb(33, 38, 45);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 10);
        Controls.Add(new Label { Text = "按需逐级揭示", Bounds = new Rectangle(25, 23, 500, 35),
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold) });
        text.Bounds = new Rectangle(25, 78, 550, 75);
        text.Text = entry.Levels[0];
        Controls.Add(text);
        levelText.Bounds = new Rectangle(25, 164, 350, 28);
        levelText.Text = "提示级别 0";
        Controls.Add(levelText);
        next.Text = "更多提示";
        next.Bounds = new Rectangle(370, 207, 110, 34);
        next.Click += (_, _) =>
        {
            if (level >= 3) return;
            level++;
            text.Text = this.entry.Levels[level];
            levelText.Text = "提示级别 " + level;
            next.Enabled = level < 3;
        };
        UiStyle.Round(next);
        Controls.Add(next);
        var close = new Button { Text = "关闭", Bounds = new Rectangle(490, 207, 85, 34) };
        close.Click += (_, _) => Close();
        UiStyle.Round(close);
        Controls.Add(close);
    }
}
