using System.Drawing.Drawing2D;

namespace WukongGuard.Installer;

internal sealed class CardListPanel : Panel
{
    private int contentHeight, offset, dragOffset = -1;
    private readonly Dictionary<Control, int> positions = new();
    internal CardListPanel() { DoubleBuffered = true; BackColor = UiStyle.Canvas; }

    internal void Reflow(int height)
    {
        contentHeight = height;
        positions.Clear();
        foreach (Control control in Controls) positions[control] = control.Top;
        offset = Math.Min(offset, MaximumOffset);
        ApplyOffset();
    }

    private int MaximumOffset => Math.Max(0, contentHeight - ClientSize.Height);
    private Rectangle Thumb
    {
        get
        {
            if (MaximumOffset == 0) return Rectangle.Empty;
            int height = Math.Max(UiStyle.Pixels(this, 36), ClientSize.Height * ClientSize.Height / contentHeight);
            int top = (ClientSize.Height - height) * offset / MaximumOffset;
            return new Rectangle(ClientSize.Width - UiStyle.Pixels(this, 8), top, UiStyle.Pixels(this, 6), height);
        }
    }

    private void ApplyOffset()
    {
        SuspendLayout();
        foreach (var entry in positions) entry.Key.Top = entry.Value - offset;
        ResumeLayout(false); Invalidate();
    }

    private void ScrollTo(int value)
    { offset = Math.Clamp(value, 0, MaximumOffset); ApplyOffset(); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(offset - e.Delta / 120 * UiStyle.Pixels(this, 60));
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (MaximumOffset > 0 && e.X >= ClientSize.Width - UiStyle.Pixels(this, 14))
        {
            if (Thumb.Contains(e.Location)) { dragOffset = e.Y - Thumb.Top; Capture = true; }
            else ScrollTo(offset + (e.Y < Thumb.Top ? -ClientSize.Height : ClientSize.Height));
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragOffset >= 0)
        {
            int travel = Math.Max(1, ClientSize.Height - Thumb.Height);
            ScrollTo((e.Y - dragOffset) * MaximumOffset / travel);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    { dragOffset = -1; Capture = false; base.OnMouseUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (MaximumOffset == 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiStyle.Path(Thumb, UiStyle.Pixels(this, 3));
        using var brush = new SolidBrush(UiStyle.Border);
        e.Graphics.FillPath(brush, path);
    }
}
