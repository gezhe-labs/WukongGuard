using System.Runtime.InteropServices;

namespace WukongGuard.Overlay;

internal static class WindowFocus
{
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr window);

    internal static bool IsForeground(IntPtr window)
    {
        var foreground = GetForegroundWindow();
        return window != IntPtr.Zero && foreground != IntPtr.Zero
            && (foreground == window || GetAncestor(foreground, 2 /* GA_ROOT */) == window);
    }
}
