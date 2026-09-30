using System.Reflection;

namespace WukongGuard.Installer;

internal static class ButtonPaintChecks
{
    // Exercise the real paint handler against a reused, dirty buffer. DrawToBitmap
    // starts with a clean bitmap and alone cannot detect unpainted corner pixels.
    internal static int Verify(Control window)
    {
        int count = 0;
        foreach (var button in Descendants(window).OfType<RoundedButton>().Where(control => control.Visible))
        {
            bool enabled = button.Enabled;
            try
            {
                button.Enabled = true;
                Call(button, "OnMouseLeave", EventArgs.Empty);
                Paint("normal");
                Call(button, "OnMouseEnter", EventArgs.Empty);
                Paint("hover");
                button.Focus();
                Paint("focus");
                Call(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, button.Width / 2, button.Height / 2, 0));
                Paint("pressed");
                Call(button, "ResetFlagsandPaint");
                button.Enabled = false;
                Paint("disabled");
                if (button.Region == null || button.Region.IsVisible(0, 0)
                    || button.Region.IsVisible(button.Width - 1, button.Height - 1)
                    || !button.Region.IsVisible(button.Width / 2, button.Height / 2))
                    throw new InvalidOperationException("Button window is not clipped to rounded corners: " + button.Text);
                count++;
            }
            finally
            {
                button.Enabled = enabled;
                Call(button, "OnMouseLeave", EventArgs.Empty);
                Call(button, "ResetFlagsandPaint");
            }

            void Paint(string state)
            {
                using var bitmap = new Bitmap(button.Width, button.Height);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.Fuchsia);
                using var args = new PaintEventArgs(graphics, button.ClientRectangle);
                Call(button, "OnPaint", args);
                foreach (var point in new[] { Point.Empty, new Point(button.Width - 1, 0),
                    new Point(0, button.Height - 1), new Point(button.Width - 1, button.Height - 1) })
                    if (bitmap.GetPixel(point.X, point.Y).ToArgb() == Color.Fuchsia.ToArgb())
                        throw new InvalidOperationException($"Unpainted button corner: {button.Text}, {state}, {point}");
            }
        }
        return count;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Call(RoundedButton button, string method, params object[] arguments) =>
        typeof(RoundedButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, arguments);
}
