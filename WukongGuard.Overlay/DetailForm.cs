using System.Drawing;
using System.Drawing.Drawing2D;

namespace WukongGuard.Overlay;

internal sealed class DetailForm : Form
{
    private readonly string[] levels;
    private readonly IntPtr returnWindow;
    private readonly bool restoreGameFocus;
    private readonly Label message = new();
    private readonly Label levelLabel = new();
    private readonly Button more = new();
    private readonly Button dismiss = new();
    private readonly System.Windows.Forms.Timer gamepadTimer = new() { Interval = 60 };
    private readonly ushort[] previousButtons = new ushort[4];
    private int level;
    private bool foregroundOnClose;

    internal DetailForm(string heading, string[] spoilerLevels, Rectangle screen,
        IntPtr gameWindow, bool restoreGameFocus)
    {
        levels = (string[])spoilerLevels.Clone();
        returnWindow = gameWindow;
        this.restoreGameFocus = restoreGameFocus;
        Text = "后悔药 · 更多提示";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        Width = 630;
        Height = 190;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + 80);
        BackColor = Color.FromArgb(27, 24, 20);
        ForeColor = Color.FromArgb(245, 229, 199);
        Font = new Font("Microsoft YaHei UI", 10f);
        Opacity = 0.97;

        var title = new Label
        {
            Text = heading,
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(225, 183, 104),
            Location = new Point(20, 12), Size = new Size(585, 25)
        };
        message.Text = levels[0];
        message.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
        message.SetBounds(20, 45, 588, 62);
        levelLabel.Text = "提示级别 0 · 点击“更多提示”逐步揭示";
        levelLabel.ForeColor = Color.FromArgb(173, 163, 145);
        levelLabel.SetBounds(20, 112, 575, 22);
        var inputHint = new Label
        {
            Text = "Enter / 手柄 A：更多提示     Esc / 手柄 B：返回游戏",
            ForeColor = Color.FromArgb(195, 174, 140),
            Font = new Font(Font.FontFamily, 9f),
            Location = new Point(20, 146), Size = new Size(375, 24)
        };
        ConfigureButton(more, "更多提示", new Rectangle(415, 144, 105, 31),
            Color.FromArgb(67, 51, 33), Color.FromArgb(185, 143, 77));
        ConfigureButton(dismiss, "关闭", new Rectangle(528, 144, 80, 31),
            Color.FromArgb(49, 44, 37), Color.FromArgb(125, 111, 91));
        more.Click += (_, _) => RevealNext();
        dismiss.Click += (_, _) => Close();
        Controls.AddRange(new Control[] { title, message, levelLabel, inputHint, more, dismiss });
        Resize += (_, _) => SetRoundedRegion(this, 18);
        more.Resize += (_, _) => SetRoundedRegion(more, 10);
        dismiss.Resize += (_, _) => SetRoundedRegion(dismiss, 10);
        SetRoundedRegion(this, 18);
        SetRoundedRegion(more, 10);
        SetRoundedRegion(dismiss, 10);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Enter or Keys.Space)
            {
                RevealNext();
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Close();
                e.SuppressKeyPress = true;
            }
        };
        gamepadTimer.Tick += (_, _) => PollGamepad();
        Shown += (_, _) =>
        {
            for (uint index = 0; index < 4; index++)
                if (GamepadInput.TryGetButtons(index, out var buttons)) previousButtons[index] = buttons;
            Activate();
            bool activated = WindowFocus.SetForegroundWindow(Handle);
            OverlayLog.Write($"detail shown window={Handle} foreground={WindowFocus.GetForegroundWindow()} activated={activated}");
            gamepadTimer.Start();
        };
        FormClosing += (_, _) => foregroundOnClose = WindowFocus.IsForeground(Handle);
        FormClosed += (_, _) =>
        {
            gamepadTimer.Stop();
            gamepadTimer.Dispose();
            bool restored = restoreGameFocus && foregroundOnClose && WindowFocus.IsWindow(returnWindow)
                && WindowFocus.SetForegroundWindow(returnWindow);
            OverlayLog.Write($"detail closed foreground_on_close={foregroundOnClose} "
                + $"restored={restored} foreground={WindowFocus.GetForegroundWindow()}");
        };
    }

    private void PollGamepad()
    {
        bool foreground = WindowFocus.IsForeground(Handle);
        for (uint index = 0; index < 4; index++)
        {
            if (!GamepadInput.TryGetButtons(index, out var buttons))
            {
                previousButtons[index] = 0;
                continue;
            }
            var pressed = (ushort)(buttons & ~previousButtons[index]);
            previousButtons[index] = buttons;
            if (!foreground) continue;
            if ((pressed & GamepadInput.B) != 0) { Close(); return; }
            if ((pressed & GamepadInput.A) != 0) { RevealNext(); return; }
        }
    }

    private void RevealNext()
    {
        if (level >= 3) return;
        level++;
        message.Text = levels[level];
        levelLabel.Text = $"提示级别 {level}";
        more.Enabled = level < 3;
    }

    private static void ConfigureButton(Button button, string text, Rectangle bounds,
        Color background, Color border)
    {
        button.Text = text;
        button.Bounds = bounds;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.BorderColor = border;
        button.BackColor = background;
        button.ForeColor = Color.FromArgb(245, 229, 199);
        button.UseVisualStyleBackColor = false;
        button.Paint += (_, e) => DrawRoundedButtonBorder(button, e, 10);
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
}
