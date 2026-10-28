using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Imaging;

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
    /// <paramref name="padding"/> is the distance from each element to its outline (negative: inside). Without
    /// <paramref name="outline"/> only the number badges are drawn, placed as if the outline were there.
    /// </summary>
    public static void Annotate(Bitmap bmp, Point origin, IReadOnlyList<Rectangle> targets, int? firstNumber, int padding = 3, bool outline = true)
    {
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
            var pad = Math.Max(padding, -(Math.Min(local.Width, local.Height) - 2) / 2);
            local.Inflate(pad, pad);
            return local;
        }).ToList();
        using var leader = new Pen(HighlightColor, 2f);
        var placed = new List<Rectangle>();
        for (var i = 0; i < outlines.Count; i++)
        {
            var local = outlines[i];
            if (local.IsEmpty) continue;
            if (outline) g.DrawRectangle(pen, local);

            if (firstNumber is null) continue;
            const int d = 24;
            var label = (firstNumber.Value + i).ToString();
            // One digit fits in a circle; longer numbers get a pill wide enough for the text
            var w = Math.Max(d, (int)Math.Ceiling(g.MeasureString(label, font, PointF.Empty, fmt).Width) + 10);
            var others = outlines.Where((o, j) => j != i && !o.IsEmpty).ToList();
            var (bx, by) = BadgePosition(local, w, d, bmp.Size, placed, others, centered: !outline);
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
