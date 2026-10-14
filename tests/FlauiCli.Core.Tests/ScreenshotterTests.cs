using System.Drawing;
using FlauiCli.Core.Imaging;

namespace FlauiCli.Core.Tests;

public sealed class ScreenshotterTests
{
    /// <summary>Red pixels in the band just above the outline, where only the number badge is drawn.</summary>
    private static int BadgeArea(int number)
    {
        using var bmp = new Bitmap(300, 200);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        // The outline is inflated to (97, 97); the badge is centred on that corner
        Screenshotter.Annotate(bmp, Point.Empty, [new Rectangle(100, 100, 40, 20)], number);

        var count = 0;
        for (var y = 80; y < 95; y++)
        {
            for (var x = 60; x < 160; x++)
            {
                var p = bmp.GetPixel(x, y);
                if (p.R > 180 && p.G < 120 && p.B < 120) count++;
            }
        }
        return count;
    }

    [Fact]
    public void TwoDigitBadgesAreWiderThanOneDigitBadges()
    {
        var one = BadgeArea(9);
        var two = BadgeArea(12);
        Assert.True(one > 0);
        Assert.True(two > one + 20, $"badge 12 covers {two} px, badge 9 covers {one} px");
    }
}
