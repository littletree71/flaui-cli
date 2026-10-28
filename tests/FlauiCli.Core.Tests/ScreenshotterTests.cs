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
    public void BadgesOfAdjacentElementsStayOnTheirOwnOutline()
    {
        // A tool strip at the top of the window: the corners above do not fit, and the corners below sit between buttons
        var image = new Size(400, 200);
        AssertBadgesOwned(image, Enumerable.Range(0, 6).Select(i => Rectangle.Inflate(new Rectangle(50 + i * 24, 0, 24, 24), 3, 3)).ToList());
    }

    /// <summary>The button grid of issue #18: two tight rows of small buttons near the left edge of a small window.</summary>
    private static readonly Rectangle[] ButtonGrid =
    [
        new(11, 56, 58, 26), new(71, 56, 58, 26), new(131, 56, 58, 26), new(11, 82, 58, 26), new(71, 82, 58, 26),
    ];

    [Fact]
    public void BadgesInAButtonGridStayOnTheirOwnOutline()
    {
        // With the default padding the outlines overlap; with a negative padding they sit inside the buttons
        AssertBadgesOwned(new Size(202, 137), [.. ButtonGrid.Select(r => Rectangle.Inflate(r, 3, 3))]);
        AssertBadgesOwned(new Size(202, 137), [.. ButtonGrid.Select(r => Rectangle.Inflate(r, -3, -3))]);
    }

    [Fact]
    public void NegativePaddingKeepsOutlinesOfTouchingElementsApart()
    {
        using var bmp = new Bitmap(202, 137);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        Screenshotter.Annotate(bmp, Point.Empty, ButtonGrid, 1, padding: -3);

        // One ends at x=69 and Two starts at x=71: neither outline reaches into the space where the buttons meet
        static bool Red(Color p) => p.R > 180 && p.G < 120 && p.B < 120;
        for (var x = 68; x <= 71; x++)
            Assert.False(Red(bmp.GetPixel(x, 69)), $"an outline crosses the gap between One and Two at x={x}");
    }

    [Fact]
    public void WithoutOutlinesOnlyTheBadgesAreDrawn()
    {
        using var bmp = new Bitmap(202, 137);
        using (var g = Graphics.FromImage(bmp)) g.Clear(Color.White);
        Screenshotter.Annotate(bmp, Point.Empty, ButtonGrid, 1, outline: false);

        static bool Red(Color p) => p.R > 180 && p.G < 120 && p.B < 120;
        var red = 0;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                if (Red(bmp.GetPixel(x, y))) red++;
            }
        }
        Assert.True(red > 0, "no badges were drawn");
        // The left edge of One's outline (x = 8, halfway down the button) stays white
        Assert.False(Red(bmp.GetPixel(8, 69)), "an outline was drawn");
    }

    [Fact]
    public void WithoutAnOutlineTheBadgeSitsAboveTheMiddle()
    {
        var outline = new Rectangle(97, 97, 46, 26);
        Assert.Equal((97 + (46 - 24) / 2, 97 - 24 + 4), Screenshotter.BadgePosition(outline, 24, 24, new Size(300, 200), [], centered: true));
    }

    private static void AssertBadgesOwned(Size image, IReadOnlyList<Rectangle> outlines)
    {
        var placed = new List<Rectangle>();
        for (var i = 0; i < outlines.Count; i++)
        {
            var others = outlines.Where((_, j) => j != i).ToList();
            var (x, y) = Screenshotter.BadgePosition(outlines[i], 24, 24, image, placed, others);
            var badge = new Rectangle(x, y, 24, 24);
            Assert.DoesNotContain(placed, p => p.IntersectsWith(badge));
            Assert.True(new Rectangle(Point.Empty, image).Contains(badge), $"badge {i + 1} at {badge} is outside the image");

            // The badge centre is nearer to its own button than to any other, and it does not cover another button
            var c = new Point(x + 12, y + 12);
            double Dist(Rectangle r) => Math.Sqrt(Math.Pow(Math.Clamp(c.X, r.Left, r.Right) - c.X, 2) + Math.Pow(Math.Clamp(c.Y, r.Top, r.Bottom) - c.Y, 2));
            Assert.All(others, o => Assert.True(Dist(o) > Dist(outlines[i]) && !Rectangle.Inflate(o, -6, -6).IntersectsWith(badge),
                $"badge {i + 1} at {badge} looks like it belongs to {o}"));
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
