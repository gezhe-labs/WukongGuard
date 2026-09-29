using System.Drawing.Drawing2D;

namespace WukongGuard.Installer;

internal static class UiStyle
{
    internal static void Round(Button button, int radius = 9)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Resize += (_, _) => ApplyRegion(button, radius);
        button.Paint += (_, e) =>
        {
            using var path = Path(new Rectangle(0, 0, button.Width - 1, button.Height - 1), radius);
            using var pen = new Pen(button.Enabled ? Color.FromArgb(105, 113, 121)
                : Color.FromArgb(67, 72, 77));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        };
        ApplyRegion(button, radius);
    }

    private static void ApplyRegion(Control control, int radius)
    {
        if (control.Width < 2 || control.Height < 2) return;
        using var path = Path(new Rectangle(0, 0, control.Width, control.Height), radius);
        var previous = control.Region;
        control.Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath Path(Rectangle bounds, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
