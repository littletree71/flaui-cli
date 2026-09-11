using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Snapshot;
using FlauiCli.Core.Tests.Fakes;

namespace FlauiCli.Core.Tests;

public class SnapshotTests
{
    private static FakeElement Sample()
    {
        return new FakeElement(ControlKind.Window, "App").Add(
            new FakeElement(ControlKind.Pane).Add(                        // 純容器：應被攤平
                new FakeElement(ControlKind.Button, "OK", "okButton"),
                new FakeElement(ControlKind.CheckBox, "Agree") { Toggle = ToggleValue.On }),
            new FakeElement(ControlKind.Separator),                        // 雜訊：應略過
            new FakeElement(ControlKind.Edit, "Name", "nameInput") { Value = "Alice", HasFocus = true },
            new FakeElement(ControlKind.Button, "Hidden") { IsOffscreen = true },
            new FakeElement(ControlKind.Button, "Off", "offButton") { IsEnabled = false },
            new FakeElement(ControlKind.TreeItem, "Node") { Expand = ExpandValue.Collapsed });
    }

    [Fact]
    public void 格式化輸出()
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
    public void All選項包含容器與畫面外元素()
    {
        var text = SnapshotFormatter.Format(Sample().CaptureTree(), new RefRegistry(), new SnapshotOptions(All: true));
        Assert.Contains("- pane", text);
        Assert.Contains("\"Hidden\"", text);
        Assert.Contains("- separator", text);
    }

    [Fact]
    public void 深度限制()
    {
        var text = SnapshotFormatter.Format(Sample().CaptureTree(), new RefRegistry(), new SnapshotOptions(MaxDepth: 1));
        Assert.Equal("- window \"App\" [ref=e1] …", text);
    }

    [Fact]
    public void Boxes選項()
    {
        var el = new FakeElement(ControlKind.Button, "B") { Bounds = new(1, 2, 3, 4) };
        var text = SnapshotFormatter.Format(el.CaptureTree(), new RefRegistry(), new SnapshotOptions(Boxes: true));
        Assert.EndsWith("[box=1,2,3,4]", text);
    }

    [Fact]
    public void 同一元素在多次snapshot間保持相同ref()
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
    public void 長文字與換行被跳脫與截斷()
    {
        var escaped = SnapshotFormatter.Escape("line1\nline2 \"q\"");
        Assert.Equal("line1\\nline2 \\\"q\\\"", escaped);
        Assert.EndsWith("…", SnapshotFormatter.Escape(new string('x', 500)));
    }
}
