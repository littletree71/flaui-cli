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
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (var i = 0; i < targets.Count; i++)
        {
            var r = targets[i];
            if (r.IsEmpty) continue;
            var local = new Rectangle(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height);
            local.Inflate(3, 3);
            g.DrawRectangle(pen, local);

            if (firstNumber is null) continue;
            const int d = 24;
            // The number badge sits just outside the top-left corner of the outline, moved inwards when it would leave the image
            var bx = Math.Clamp(local.X - d / 2, 0, Math.Max(0, bmp.Width - d));
            var by = Math.Clamp(local.Y - d / 2, 0, Math.Max(0, bmp.Height - d));
            g.FillEllipse(brush, bx, by, d, d);
            g.DrawString((firstNumber.Value + i).ToString(), font, Brushes.White, new RectangleF(bx, by, d, d), fmt);
        }
    }

    public static string SavePng(Bitmap bmp, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }
}
