using System.Drawing.Drawing2D;

namespace WukongGuard.Installer;

internal sealed class GameCard : Panel
{
    private Image? artwork;
    private readonly Label gameName = UiStyle.Label("黑神话：悟空", 24, true);
    private readonly Label platform = UiStyle.Label("WINDOWS  /  STEAM", 9, true, UiStyle.Muted);
    private readonly Label subtitle = UiStyle.Label("把遗憾留在游戏之外", 11, false, UiStyle.Muted);
    private readonly Label status = UiStyle.Label("保护尚未启动", 12, true, UiStyle.Muted);
    private readonly Label hint = UiStyle.Label("点击下方「启动保护」，再从 Steam 启动游戏。", 10, false, UiStyle.Muted);
    private readonly Label pathTitle = UiStyle.Label("游戏目录", 9, false, UiStyle.Muted);
    private readonly Label path = UiStyle.Label("自动识别游戏目录", 9);
    private readonly RoundedButton configure = UiStyle.Button("游戏配置");
    private readonly RoundedButton browse = UiStyle.Button("选择目录");
    private readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    internal event EventHandler? ConfigureRequested;
    internal event EventHandler? BrowseRequested;
    internal bool DirectoryEditable { set => browse.Enabled = value; }
    internal string FullPath { get; private set; } = "";
    internal bool HasArtwork => artwork != null;
    internal bool Compact { get; set; }
    internal string StatusText => status.Text;

    internal GameCard()
    {
        BackColor = UiStyle.Canvas;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        configure.AccessibleName = "黑神话悟空游戏配置";
        configure.Click += (_, e) => ConfigureRequested?.Invoke(this, e);
        browse.Click += (_, e) => BrowseRequested?.Invoke(this, e);
        path.Cursor = Cursors.Hand;
        path.Click += (_, _) =>
        {
            if (FullPath.Length == 0) return;
            try { Clipboard.SetText(FullPath); tips.Show("完整路径已复制", path, 1800); }
            catch (System.Runtime.InteropServices.ExternalException) { }
        };
        Controls.AddRange(new Control[] { platform, gameName, subtitle, configure, status, hint,
            pathTitle, path, browse });
    }

    internal void SetGamePath(string? value)
    {
        FullPath = value ?? "";
        RefreshPathText();
        tips.SetToolTip(path, FullPath.Length == 0 ? "选择包含游戏的 BlackMythWukong 文件夹" : FullPath + "\n点击复制完整路径");
        artwork?.Dispose();
        artwork = GameArtwork.Load(value);
        Invalidate();
    }

    internal void SetStatus(StagePresentation presentation, string? detail = null)
    {
        status.Text = presentation.Heading;
        status.ForeColor = presentation.Color;
        hint.Text = detail ?? (presentation.Heading == "保护尚未启动"
            ? "点击下方「启动保护」，再从 Steam 启动游戏。" : presentation.Guidance);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (configure == null) return;
        int P(int n) => UiStyle.Pixels(this, n);
        int inset = P(26), right = Width - inset, hero = P(Compact ? 112 : 200);
        platform.SetBounds(inset, P(34), Width - P(180), P(24));
        gameName.SetBounds(inset, P(Compact ? 64 : 68), Width - P(55), P(Compact ? 45 : 50));
        subtitle.SetBounds(inset, P(Compact ? 119 : 132), Width - P(55), P(28));
        subtitle.Visible = !Compact;
        configure.SetBounds(right - P(112), P(24), P(112), P(38));
        status.SetBounds(inset, hero + P(20), Width - inset * 2, P(30));
        hint.SetBounds(inset, hero + P(56), Width - inset * 2, P(43));
        pathTitle.SetBounds(inset, hero + P(108), P(100), P(22));
        path.SetBounds(inset, hero + P(138), Width - inset * 2 - P(118), P(24));
        browse.SetBounds(right - P(104), hero + P(128), P(104), P(38));
        RefreshPathText();
    }

    private void RefreshPathText()
    {
        if (FullPath.Length == 0) { path.Text = "尚未识别，请选择 BlackMythWukong 文件夹"; return; }
        if (TextRenderer.MeasureText(FullPath, path.Font).Width <= path.Width) { path.Text = FullPath; return; }
        var parts = FullPath.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar);
        path.Text = Path.GetPathRoot(FullPath) + "…\\" + string.Join("\\", parts.TakeLast(3));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        float scale = UiStyle.ScaleFactor(this);
        float heroHeight = (Compact ? 112 : 200) * scale;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = UiStyle.Path(new RectangleF(.5f, .5f, Width - 1, Height - 1), 16 * scale);
        var saved = e.Graphics.Save();
        e.Graphics.SetClip(outline);
        using var surface = new SolidBrush(UiStyle.Surface);
        e.Graphics.FillRectangle(surface, ClientRectangle);
        var hero = new RectangleF(0, 0, Width, heroHeight);
        e.Graphics.SetClip(hero, CombineMode.Intersect);
        if (artwork != null)
        {
            // Fit the wide Steam hero without moving its character behind the title.
            float factor = Math.Max(Width / (float)artwork.Width, heroHeight / artwork.Height);
            float width = artwork.Width * factor, height = artwork.Height * factor;
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(artwork, new RectangleF((Width - width) / 2, (heroHeight - height) / 2, width, height));
        }
        else
        {
            using var fallback = new LinearGradientBrush(hero, Color.FromArgb(67, 51, 33), UiStyle.Surface, 0f);
            e.Graphics.FillRectangle(fallback, hero);
            using var ring = new Pen(Color.FromArgb(35, UiStyle.Accent), 2 * scale);
            e.Graphics.DrawEllipse(ring, Width - 200 * scale, -35 * scale, 280 * scale, 280 * scale);
        }
        using var shade = new LinearGradientBrush(hero, Color.FromArgb(220, 17, 21, 27),
            Color.FromArgb(30, 17, 21, 27), 0f);
        e.Graphics.FillRectangle(shade, hero);
        using var fade = new LinearGradientBrush(hero, Color.FromArgb(0, UiStyle.Surface), UiStyle.Surface, 90);
        fade.InterpolationColors = new ColorBlend
        {
            Colors = new[] { Color.FromArgb(0, UiStyle.Surface), Color.FromArgb(0, UiStyle.Surface), UiStyle.Surface },
            Positions = new[] { 0f, .68f, 1f }
        };
        e.Graphics.FillRectangle(fade, hero);
        e.Graphics.Restore(saved);
        using var pen = new Pen(UiStyle.Border);
        e.Graphics.DrawPath(pen, outline);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { artwork?.Dispose(); tips.Dispose(); }
        base.Dispose(disposing);
    }
}
