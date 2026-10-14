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

    /// <summary>Outlines elements on the image. <paramref name="origin"/> is the screen position of the image's top-left corner.</summary>
    public static void Annotate(Bitmap bmp, Point origin, IReadOnlyList<Rectangle> targets, int? firstNumber)
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

        for (var i = 0; i < targets.Count; i++)
        {
            var r = targets[i];
            if (r.IsEmpty) continue;
            var local = new Rectangle(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height);
            local.Inflate(3, 3);
            g.DrawRectangle(pen, local);

            if (firstNumber is null) continue;
            const int d = 24;
            var label = (firstNumber.Value + i).ToString();
            // One digit fits in a circle; longer numbers get a pill wide enough for the text
            var w = Math.Max(d, (int)Math.Ceiling(g.MeasureString(label, font, PointF.Empty, fmt).Width) + 10);
            var (bx, by) = BadgePosition(local, w, d, bmp.Size);
            FillBadge(g, brush, bx, by, w, d);
            g.DrawString(label, font, Brushes.White, new RectangleF(bx, by, w, d), fmt);
        }
    }

    /// <summary>
    /// Places the number badge outside the outline so it never covers the element itself (for example the text of a
    /// narrow input): diagonally off the top-left corner, else off another corner that fits in the image, else clamped.
    /// </summary>
    private static (int X, int Y) BadgePosition(Rectangle outline, int width, int height, Size image)
    {
        const int touch = 4; // overlap with the outline so the badge visibly belongs to it
        (int X, int Y)[] candidates =
        [
            (outline.Left - width + touch, outline.Top - height + touch),
            (outline.Right - touch, outline.Top - height + touch),
            (outline.Left - width + touch, outline.Bottom - touch),
            (outline.Right - touch, outline.Bottom - touch),
        ];
        foreach (var (x, y) in candidates)
        {
            if (x >= 0 && y >= 0 && x + width <= image.Width && y + height <= image.Height) return (x, y);
        }
        var (fx, fy) = candidates[0];
        return (Math.Clamp(fx, 0, Math.Max(0, image.Width - width)), Math.Clamp(fy, 0, Math.Max(0, image.Height - height)));
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
