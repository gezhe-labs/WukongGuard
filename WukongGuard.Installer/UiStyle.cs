using System.Drawing.Drawing2D;

namespace WukongGuard.Installer;

internal static class UiStyle
{
    // An isolated renderer can exercise enlarged metrics without changing Windows settings.
    internal static float PreviewScale { get; set; } = 1f;
    internal static float ScaleFactor(Control control) => control.DeviceDpi / 96f * PreviewScale;
    internal static int Pixels(Control control, int value) => (int)Math.Round(value * ScaleFactor(control));
    internal static readonly Color Canvas = Color.FromArgb(20, 23, 29);
    internal static readonly Color Surface = Color.FromArgb(30, 35, 43);
    internal static readonly Color Raised = Color.FromArgb(41, 47, 57);
    internal static readonly Color Text = Color.FromArgb(241, 243, 247);
    internal static readonly Color Muted = Color.FromArgb(174, 184, 199);
    internal static readonly Color Accent = Color.FromArgb(249, 194, 99);
    internal static readonly Color Border = Color.FromArgb(65, 74, 87);
    internal static readonly Color Success = Color.FromArgb(135, 214, 169);
    internal static readonly Color Error = Color.FromArgb(244, 160, 151);

    internal static RoundedButton Button(string text, bool primary = false) => new()
    {
        Text = text, Primary = primary, Height = 42,
        Font = new Font("Microsoft YaHei UI", 10f * PreviewScale, FontStyle.Bold)
    };

    internal static Label Label(string text, float size = 10, bool bold = false,
        Color? color = null) => new()
    {
        Text = text, ForeColor = color ?? Text, BackColor = Color.Transparent,
        Font = new Font("Microsoft YaHei UI", size * PreviewScale, bold ? FontStyle.Bold : FontStyle.Regular),
        AutoEllipsis = true
    };

    internal static void Round(Button button, int radius = 10)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.BackColor = Raised;
        button.ForeColor = Text;
        button.UseVisualStyleBackColor = false;
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

    internal static void ApplyRegion(Control control, float radius)
    {
        if (control.Width < 2 || control.Height < 2) return;
        using var path = Path(new Rectangle(0, 0, control.Width, control.Height), radius);
        var previous = control.Region;
        control.Region = new Region(path);
        previous?.Dispose();
    }

    internal static GraphicsPath Path(RectangleF bounds, float radius)
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

internal sealed class RoundedButton : Button
{
    private bool primary, hovered, pressed;
    internal bool Primary
    {
        get => primary;
        set { primary = value; Invalidate(); }
    }

    internal RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Opaque, false);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Hand;
        UpdateRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    { base.OnSizeChanged(e); UpdateRoundedRegion(); }

    protected override void OnHandleCreated(EventArgs e)
    { base.OnHandleCreated(e); UpdateRoundedRegion(); }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    { base.OnDpiChangedAfterParent(e); UpdateRoundedRegion(); }

    private void UpdateRoundedRegion() => UiStyle.ApplyRegion(this, 10f * UiStyle.ScaleFactor(this));

    // Compose the background and button in one pass; inherited Button painting
    // treats the whole rectangle as opaque and otherwise leaves its corners dirty.
    protected override void OnPaintBackground(PaintEventArgs e) { }

    private void PaintParentBackground(PaintEventArgs e)
    {
        if (Parent == null) { e.Graphics.Clear(UiStyle.Canvas); return; }
        var saved = e.Graphics.Save();
        try
        {
            e.Graphics.TranslateTransform(-Left, -Top);
            var clip = e.ClipRectangle;
            clip.Offset(Left, Top);
            e.Graphics.SetClip(clip, CombineMode.Intersect);
            using var parentPaint = new PaintEventArgs(e.Graphics, clip);
            InvokePaintBackground(Parent, parentPaint);
            InvokePaint(Parent, parentPaint);
        }
        finally { e.Graphics.Restore(saved); }
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { pressed = false; Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintParentBackground(e);
        if (Width < 2 || Height < 2) return;
        var fill = !Enabled ? UiStyle.Raised : primary ? UiStyle.Accent : UiStyle.Raised;
        if (Enabled && (hovered || pressed))
        {
            int change = pressed ? -16 : 12;
            fill = Color.FromArgb(Math.Clamp(fill.R + change, 0, 255),
                Math.Clamp(fill.G + change, 0, 255), Math.Clamp(fill.B + change, 0, 255));
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = UiStyle.ScaleFactor(this);
        float stroke = (Focused && ShowFocusCues ? 2f : 1f) * scale;
        float inset = stroke / 2;
        using var path = UiStyle.Path(new RectangleF(inset, inset, Width - stroke, Height - stroke),
            Math.Max(.5f, 10f * scale - inset));
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(Focused && ShowFocusCues ? UiStyle.Accent : primary && Enabled ? fill : UiStyle.Border,
            stroke);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            !Enabled ? UiStyle.Muted : primary ? UiStyle.Canvas : UiStyle.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

internal class SurfacePanel : Panel
{
    internal Color SurfaceColor { get; set; } = UiStyle.Surface;
    internal Color OutlineColor { get; set; } = UiStyle.Border;
    internal SurfacePanel()
    {
        BackColor = UiStyle.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiStyle.Path(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16f * UiStyle.ScaleFactor(this));
        using var brush = new SolidBrush(SurfaceColor);
        using var pen = new Pen(OutlineColor);
        e.Graphics.FillPath(brush, path);
        e.Graphics.DrawPath(pen, path);
    }
}
