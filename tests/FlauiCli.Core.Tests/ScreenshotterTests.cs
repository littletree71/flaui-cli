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
        // The outline is inflated to (97, 97); the badge sits above-left of that corner
        Screenshotter.Annotate(bmp, Point.Empty, [new Rectangle(100, 100, 40, 20)], number);

        var count = 0;
        for (var y = 74; y < 95; y++)
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
    public void BadgeDoesNotCoverTheElement()
    {
        using var bmp = new Bitmap(300, 200);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        var target = new Rectangle(100, 100, 40, 20);
        Screenshotter.Annotate(bmp, Point.Empty, [target], 12);

        // The inside of the target (where a text box shows its value) stays untouched
        for (var y = target.Top + 2; y < target.Bottom - 2; y++)
        {
            for (var x = target.Left + 2; x < target.Right - 2; x++)
                Assert.Equal(Color.White.ToArgb(), bmp.GetPixel(x, y).ToArgb());
        }
    }

    [Fact]
    public void BadgeStaysInsideTheImageForElementsAtTheEdge()
    {
        using var bmp = new Bitmap(300, 200);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        var target = new Rectangle(0, 0, 40, 20);
        Screenshotter.Annotate(bmp, Point.Empty, [target], 3);

        // No room above-left: the badge goes below or to the right of the element instead of over it
        for (var y = target.Top + 4; y < target.Bottom - 2; y++)
        {
            for (var x = target.Left + 4; x < target.Right - 2; x++)
                Assert.Equal(Color.White.ToArgb(), bmp.GetPixel(x, y).ToArgb());
        }
    }

    [Fact]
    public void TwoDigitBadgesAreWiderThanOneDigitBadges()
    {
        var one = BadgeArea(9);
        var two = BadgeArea(12);
        Assert.True(one > 0);
        Assert.True(two > one + 20, $"badge 12 covers {two} px, badge 9 covers {one} px");
    }

    [Fact]
    public void BadgesOfAdjacentElementsDoNotOverlap()
    {
        // A row of toolbar buttons that touch each other, outlined like Annotate does (inflated by 3)
        var image = new Size(400, 200);
        var placed = new List<Rectangle>();
        for (var i = 0; i < 6; i++)
        {
            var outline = new Rectangle(50 + i * 24, 100, 24, 24);
            outline.Inflate(3, 3);
            var (x, y) = Screenshotter.BadgePosition(outline, 24, 24, image, placed);
            var badge = new Rectangle(x, y, 24, 24);
            Assert.DoesNotContain(placed, p => p.IntersectsWith(badge));
            Assert.True(new Rectangle(Point.Empty, image).Contains(badge));
            placed.Add(badge);
        }
    }

    [Fact]
    public void FirstBadgeKeepsTheTopLeftCorner()
    {
        var outline = new Rectangle(97, 97, 46, 26);
        Assert.Equal((97 - 24 + 4, 97 - 24 + 4), Screenshotter.BadgePosition(outline, 24, 24, new Size(300, 200), []));
    }
}
