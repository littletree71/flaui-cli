using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Snapshot;
using FlauiCli.Core.Tests.Fakes;

namespace FlauiCli.Core.Tests;

public class SnapshotTests
{
    private static FakeElement Sample()
    {
        return new FakeElement(ControlKind.Window, "App").Add(
            new FakeElement(ControlKind.Pane).Add(                        // pure container: flattened
                new FakeElement(ControlKind.Button, "OK", "okButton"),
                new FakeElement(ControlKind.CheckBox, "Agree") { Toggle = ToggleValue.On }),
            new FakeElement(ControlKind.Separator),                        // noise: skipped
            new FakeElement(ControlKind.Edit, "Name", "nameInput") { Value = "Alice", HasFocus = true },
            new FakeElement(ControlKind.Button, "Hidden") { IsOffscreen = true },
            new FakeElement(ControlKind.Button, "Off", "offButton") { IsEnabled = false },
            new FakeElement(ControlKind.TreeItem, "Node") { Expand = ExpandValue.Collapsed });
    }

    [Fact]
    public void FormatsTheTree()
    {
        var refs = new RefRegistry();
        var text = SnapshotFormatter.Format(Sample().CaptureTree(), refs, new SnapshotOptions());
        var expected = """
            - window "App" [ref=e1]
              - button "OK" [ref=e2] id=okButton
              - checkbox "Agree" [ref=e3] [checked]
              - edit "Name" [ref=e4] id=nameInput [focused] value="Alice"
              - button "Off" [ref=e5] id=offButton [disabled]
              - treeitem "Node" [ref=e6] [collapsed]
            """;
        Assert.Equal(expected.ReplaceLineEndings(), text.ReplaceLineEndings());
    }

    [Fact]
    public void AllOptionIncludesContainersAndOffscreenElements()
    {
        var text = SnapshotFormatter.Format(Sample().CaptureTree(), new RefRegistry(), new SnapshotOptions(All: true));
        Assert.Contains("- pane", text);
        Assert.Contains("\"Hidden\"", text);
        Assert.Contains("- separator", text);
    }

    [Fact]
    public void DepthLimit()
    {
        var text = SnapshotFormatter.Format(Sample().CaptureTree(), new RefRegistry(), new SnapshotOptions(MaxDepth: 1));
        Assert.Equal("- window \"App\" [ref=e1] …", text);
    }

    [Fact]
    public void BoxesOption()
    {
        var el = new FakeElement(ControlKind.Button, "B") { Bounds = new(1, 2, 3, 4) };
        var text = SnapshotFormatter.Format(el.CaptureTree(), new RefRegistry(), new SnapshotOptions(Boxes: true));
        Assert.EndsWith("[box=1,2,3,4]", text);
    }

    [Fact]
    public void SameElementKeepsItsRefAcrossSnapshots()
    {
        var root = Sample();
        var refs = new RefRegistry();
        var first = SnapshotFormatter.Format(root.CaptureTree(), refs, new SnapshotOptions());
        root.Add(new FakeElement(ControlKind.Button, "New"));
        var second = SnapshotFormatter.Format(root.CaptureTree(), refs, new SnapshotOptions());

        Assert.Contains("button \"OK\" [ref=e2]", first);
        Assert.Contains("button \"OK\" [ref=e2]", second);
        Assert.Contains("button \"New\" [ref=e7]", second);
    }

    [Fact]
    public void LongTextAndNewLinesAreEscapedAndTruncated()
    {
        var escaped = SnapshotFormatter.Escape("line1\nline2 \"q\"");
        Assert.Equal("line1\\nline2 \\\"q\\\"", escaped);
        Assert.EndsWith("…", SnapshotFormatter.Escape(new string('x', 500)));
    }

    private static FakeElement Deep()
    {
        // Nested unnamed containers (not printed, so they do not count as a level) between printed elements
        return new FakeElement(ControlKind.Window, "App").Add(
            new FakeElement(ControlKind.Pane).Add(
                new FakeElement(ControlKind.Pane).Add(
                    new FakeElement(ControlKind.Group, "Settings").Add(
                        new FakeElement(ControlKind.Pane).Add(
                            new FakeElement(ControlKind.Edit, "Name", "nameInput"),
                            new FakeElement(ControlKind.Text).Add(new FakeElement(ControlKind.Button, "Inner"))),
                        new FakeElement(ControlKind.Text)),
                    new FakeElement(ControlKind.Button, "OK"))),
            new FakeElement(ControlKind.Tree, "Files").Add(
                new FakeElement(ControlKind.TreeItem, "src").Add(
                    new FakeElement(ControlKind.TreeItem, "a.cs").Add(new FakeElement(ControlKind.TreeItem, "A")))));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(2, true)]
    [InlineData(4, true)]
    public void DepthLimitedCapturePrintsTheSameAsAFullCapture(int depth, bool all)
    {
        var options = new SnapshotOptions(MaxDepth: depth, All: all);
        var full = SnapshotFormatter.Format(Deep().CaptureTree(), new RefRegistry(), options);
        var root = Deep();
        var limited = SnapshotFormatter.Format(root.CaptureTree(SnapshotFormatter.CaptureDepthFor(options)), new RefRegistry(), options);
        Assert.Equal(full, limited);
    }

    [Fact]
    public void DepthLimitedCaptureLeavesOutDeeperLevels()
    {
        var options = new SnapshotOptions(MaxDepth: 2);
        var tree = Deep().CaptureTree(SnapshotFormatter.CaptureDepthFor(options));
        Assert.DoesNotContain(tree.DescendantsAndSelf(), n => n.Name == "Name");
        Assert.DoesNotContain(tree.DescendantsAndSelf(), n => n.Name == "a.cs");
        Assert.Contains(tree.DescendantsAndSelf(), n => n.Name == "src"); // known to exist, so "Files" prints "…"
        Assert.Null(SnapshotFormatter.CaptureDepthFor(new SnapshotOptions()));
    }

    [Fact]
    public void CollapsedNodeIsPrintedWithoutItsChildren()
    {
        var root = Deep();
        var settings = root.Descendants().First(e => e.Name == "Settings");
        var text = SnapshotFormatter.Format(root.CaptureTree(), new RefRegistry(), new SnapshotOptions(), collapse: settings.RuntimeId);
        Assert.Contains("group \"Settings\"", text);
        Assert.Contains("(current window, shown above)", text);
        Assert.DoesNotContain("\"Name\"", text);
        Assert.Contains("button \"OK\"", text);
    }
}
