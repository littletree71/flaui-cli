using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Imaging;

/// <summary>Where number badges go relative to their outline.</summary>
internal enum BadgePlacement
{
    /// <summary>Chosen per element, away from other elements and badges.</summary>
    Auto,
    Top,
    Bottom,
    Left,
    Right,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,

    /// <summary>Inside the outline's top-left corner.</summary>
    Inside,
}

/// <summary>How highlighted elements are drawn (see <see cref="CliConfig.HighlightConfig"/>).</summary>
internal sealed record HighlightStyle(int Padding = 3, bool Outline = true, BadgePlacement Badge = BadgePlacement.Auto)
{
    public static readonly HighlightStyle Default = new();

    public const string BadgeNames = "auto, top, bottom, left, right, top-left, top-right, bottom-left, bottom-right, inside";

    public static HighlightStyle From(CliConfig.HighlightConfig config) => new(config.Padding, config.Outline, ParseBadge(config.Badge));

    /// <summary>Parses a badge position such as <c>top-right</c>.</summary>
    public static BadgePlacement ParseBadge(string? value) =>
        Enum.TryParse<BadgePlacement>((value ?? "auto").Replace("-", ""), ignoreCase: true, out var p) && Enum.IsDefined(p) && !int.TryParse(value, out _)
            ? p
            : throw new CliException($"Unknown badge position: {value} (use {BadgeNames})");
}

/// <summary>Screenshots and annotations (red outline + number). Coordinates are always physical screen pixels.</summary>
internal static class Screenshotter
{
    private static readonly Color HighlightColor = Color.FromArgb(229, 57, 53);

    /// <summary>Captures a rectangle of the screen (clipped to the virtual screen).</summary>
    public static Bitmap CaptureRegion(IScreenCapture screen, Rectangle region, out Rectangle captured)
    {
        captured = Rectangle.Intersect(region, screen.VirtualScreen);
        if (captured.Width <= 0 || captured.Height <= 0)
            throw new CliException("The region to capture is off-screen (the window may be minimized)");
        return screen.Capture(captured);
    }

    /// <summary>
    /// Outlines elements on the image. <paramref name="origin"/> is the screen position of the image's top-left corner.
    /// <see cref="HighlightStyle.Padding"/> is the distance from each element to its outline (negative: inside). Without
    /// <see cref="HighlightStyle.Outline"/> only the number badges are drawn, placed as if the outline were there.
    /// </summary>
    public static void Annotate(Bitmap bmp, Point origin, IReadOnlyList<Rectangle> targets, int? firstNumber, HighlightStyle? style = null)
    {
        style ??= HighlightStyle.Default;
        if (targets.Count == 0) return;
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var pen = new Pen(HighlightColor, 3f);
        using var brush = new SolidBrush(HighlightColor);
        using var font = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        using var fmt = new StringFormat(StringFormatFlags.NoWrap | StringFormatFlags.NoClip)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };

        var outlines = targets.Select(r =>
        {
            if (r.IsEmpty) return Rectangle.Empty;
            var local = new Rectangle(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height);
            // An outline cannot shrink past the middle of the element
            var pad = Math.Max(style.Padding, -(Math.Min(local.Width, local.Height) - 2) / 2);
            local.Inflate(pad, pad);
            return local;
        }).ToList();
        using var leader = new Pen(HighlightColor, 2f);
        var placed = new List<Rectangle>();
        for (var i = 0; i < outlines.Count; i++)
        {
            var local = outlines[i];
            if (local.IsEmpty) continue;
            if (style.Outline) g.DrawRectangle(pen, local);

            if (firstNumber is null) continue;
            const int d = 24;
            var label = (firstNumber.Value + i).ToString();
            // One digit fits in a circle; longer numbers get a pill wide enough for the text
            var w = Math.Max(d, (int)Math.Ceiling(g.MeasureString(label, font, PointF.Empty, fmt).Width) + 10);
            var others = outlines.Where((o, j) => j != i && !o.IsEmpty).ToList();
            var (bx, by) = style.Badge == BadgePlacement.Auto
                ? BadgePosition(local, w, d, bmp.Size, placed, others, centered: !style.Outline)
                : FixedBadgePosition(local, w, d, bmp.Size, style.Badge);
            var badge = new Rectangle(bx, by, w, d);
            placed.Add(badge);
            // A badge that had to move away from its outline points back to it
            if (!Touches(badge, local))
                g.DrawLine(leader, Center(badge), Nearest(local, Center(badge)));
            FillBadge(g, brush, bx, by, w, d);
            g.DrawString(label, font, Brushes.White, new RectangleF(bx, by, w, d), fmt);
        }
    }

    private static Point Center(Rectangle r) => new(r.X + r.Width / 2, r.Y + r.Height / 2);

    /// <summary>The point of <paramref name="r"/> nearest to <paramref name="p"/>.</summary>
    private static Point Nearest(Rectangle r, Point p) =>
        new(Math.Clamp(p.X, r.Left, r.Right), Math.Clamp(p.Y, r.Top, r.Bottom));

    private static double Distance(Rectangle r, Point p)
    {
        var n = Nearest(r, p);
        return Math.Sqrt(Math.Pow(n.X - p.X, 2) + Math.Pow(n.Y - p.Y, 2));
    }

    private static bool Touches(Rectangle badge, Rectangle outline) =>
        Rectangle.Inflate(outline, 1, 1).IntersectsWith(badge);

    /// <summary>
    /// Places the number badge outside the outline so it never covers the element itself (for example the text of a
    /// narrow input): diagonally off the top-left corner, else right above or below the middle of the outline, else
    /// off another corner that fits in the image.
    /// <para>
    /// Positions taken by the badges of earlier elements (<paramref name="placed"/>) are skipped, and so are positions
    /// closer to the outline of another element (<paramref name="others"/>) than to this one, or covering it, where the
    /// badge would seem to belong to the neighbour (adjacent buttons). The badge then sits on the outline's own edge
    /// (straddling the top or bottom edge, or the sides), and only when none of those is free does it move sideways
    /// (the caller then draws a leader line back to the outline).
    /// </para>
    /// <para>
    /// With <paramref name="centered"/> (no outline is drawn) the positions above and below the middle come first: a
    /// badge off a corner, without the outline, looks like it belongs to the element next to that corner.
    /// </para>
    /// </summary>
    internal static (int X, int Y) BadgePosition(Rectangle outline, int width, int height, Size image,
        IReadOnlyList<Rectangle>? placed = null, IReadOnlyList<Rectangle>? others = null, bool centered = false)
    {
        const int touch = 4; // overlap with the outline so the badge visibly belongs to it
        (int X, int Y)[] candidates =
        [
            (outline.Left - width + touch, outline.Top - height + touch),
            (outline.Right - touch, outline.Top - height + touch),
            (outline.Left - width + touch, outline.Bottom - touch),
            (outline.Right - touch, outline.Bottom - touch),
        ];
        int midX = outline.Left + (outline.Width - width) / 2, midY = outline.Top + (outline.Height - height) / 2;
        // Right above / below the middle of the outline: in a grid of buttons, the top row gets its badges above and
        // the bottom row below, where no neighbour sits between the badge and its button
        (int X, int Y)[] beside =
        [
            (midX, outline.Top - height + touch),
            (midX, outline.Bottom - touch),
        ];
        (int X, int Y)[] onEdge =
        [
            (midX, outline.Top - height / 2),
            (midX, outline.Bottom - height / 2),
            (outline.Left, outline.Top - height / 2),
            (outline.Right - width, outline.Top - height / 2),
            (outline.Left, outline.Bottom - height / 2),
            (outline.Right - width, outline.Bottom - height / 2),
            (outline.Left - width / 2, midY),
            (outline.Right - width / 2, midY),
        ];
        bool Fits(int x, int y) => x >= 0 && y >= 0 && x + width <= image.Width && y + height <= image.Height;
        bool Free(int x, int y) => placed is null || !placed.Any(p => p.IntersectsWith(new Rectangle(x, y, width, height)));
        bool Owned(int x, int y)
        {
            if (others is null) return true;
            var badge = new Rectangle(x, y, width, height);
            var center = new Point(x + width / 2, y + height / 2);
            var own = Distance(outline, center);
            // Covering another element makes the badge look like it belongs there, whatever the distances say. The band
            // just inside an outline (padding and pen) does not count: outlines of touching elements overlap there.
            return others.All(o => Distance(o, center) > own && !Rectangle.Inflate(o, -6, -6).IntersectsWith(badge));
        }

        var order = centered
            ? beside.Concat(candidates)
            : candidates.Take(1).Concat(beside).Concat(candidates.Skip(1));
        foreach (var (x, y) in order.Concat(onEdge))
        {
            if (Fits(x, y) && Free(x, y) && Owned(x, y)) return (x, y);
        }

        var fitting = candidates.Where(c => Fits(c.X, c.Y)).ToList();
        foreach (var (x, y) in fitting)
        {
            if (Free(x, y)) return (x, y);
        }

        var (fx, fy) = fitting.Count > 0 ? fitting[0] : candidates[0];
        fx = Math.Clamp(fx, 0, Math.Max(0, image.Width - width));
        fy = Math.Clamp(fy, 0, Math.Max(0, image.Height - height));
        if (Free(fx, fy)) return (fx, fy);

        // Every corner is taken: slide along the row (left first, then right), then try the row below the outline
        const int gap = 2;
        foreach (var row in new[] { fy, Math.Clamp(outline.Bottom - touch, 0, Math.Max(0, image.Height - height)) })
        {
            for (var step = 1; step <= 8; step++)
            {
                foreach (var x in new[] { fx - step * (width + gap), fx + step * (width + gap) })
                {
                    if (Fits(x, row) && Free(x, row)) return (x, row);
                }
            }
        }
        return (fx, fy);
    }

    /// <summary>
    /// The badge at a position the user chose, touching the outline from outside (or inside its top-left corner for
    /// <see cref="BadgePlacement.Inside"/>). Only moved to stay inside the image.
    /// </summary>
    internal static (int X, int Y) FixedBadgePosition(Rectangle outline, int width, int height, Size image, BadgePlacement placement)
    {
        const int touch = 4;
        int left = outline.Left - width + touch, right = outline.Right - touch;
        int top = outline.Top - height + touch, bottom = outline.Bottom - touch;
        int midX = outline.Left + (outline.Width - width) / 2, midY = outline.Top + (outline.Height - height) / 2;
        var (x, y) = placement switch
        {
            BadgePlacement.Top => (midX, top),
            BadgePlacement.Bottom => (midX, bottom),
            BadgePlacement.Left => (left, midY),
            BadgePlacement.Right => (right, midY),
            BadgePlacement.TopLeft => (left, top),
            BadgePlacement.TopRight => (right, top),
            BadgePlacement.BottomLeft => (left, bottom),
            BadgePlacement.BottomRight => (right, bottom),
            BadgePlacement.Inside => (outline.Left + 3, outline.Top + 3),
            _ => throw new ArgumentOutOfRangeException(nameof(placement)),
        };
        return (Math.Clamp(x, 0, Math.Max(0, image.Width - width)), Math.Clamp(y, 0, Math.Max(0, image.Height - height)));
    }

    private static void FillBadge(Graphics g, Brush brush, int x, int y, int width, int height)
    {
        if (width <= height)
        {
            g.FillEllipse(brush, x, y, height, height);
            return;
        }
        using var path = new GraphicsPath();
        path.AddArc(x, y, height, height, 90, 180);
        path.AddArc(x + width - height, y, height, height, 270, 180);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    public static string SavePng(Bitmap bmp, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }
}
