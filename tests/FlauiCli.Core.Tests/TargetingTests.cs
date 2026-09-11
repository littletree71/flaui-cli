using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Targeting;
using FlauiCli.Core.Tests.Fakes;

namespace FlauiCli.Core.Tests;

public class TargetingTests
{
    private static (FakeElement Root, FakeElement Ok, FakeElement DupA, FakeElement DupB, FakeElement Inner) Tree()
    {
        var ok = new FakeElement(ControlKind.Button, "OK", "okButton");
        var dupA = new FakeElement(ControlKind.Button, "Dup");
        var dupB = new FakeElement(ControlKind.Button, "Dup");
        var inner = new FakeElement(ControlKind.Button, "OK", "okButton");
        var root = new FakeElement(ControlKind.Window, "W").Add(
            new FakeElement(ControlKind.Group, "Top", "top").Add(ok, dupA, dupB),
            new FakeElement(ControlKind.Group, "Panel", "panel").Add(inner));
        return (root, ok, dupA, dupB, inner);
    }

    [Fact]
    public void FindsById()
    {
        var t = Tree();
        var found = ElementFinder.FindAll(Selector.Parse("id=okButton"), [t.Root]);
        Assert.Equal([t.Ok, t.Inner], found);
    }

    [Fact]
    public void NestingLimitsTheScope()
    {
        var t = Tree();
        var found = ElementFinder.FindAll(Selector.Parse("id=panel >> id=okButton"), [t.Root]);
        Assert.Equal([t.Inner], found);
    }

    [Fact]
    public void Nth()
    {
        var t = Tree();
        Assert.Same(t.DupB, Assert.Single(ElementFinder.FindAll(Selector.Parse("name=Dup&&nth=1"), [t.Root])));
        Assert.Empty(ElementFinder.FindAll(Selector.Parse("name=Dup&&nth=5"), [t.Root]));
    }

    [Fact]
    public void TextIsACaseInsensitiveContainsMatch()
    {
        var t = Tree();
        var found = ElementFinder.FindAll(Selector.Parse("text=pan"), [t.Root]);
        Assert.Equal("Panel", ((IUiElement)Assert.Single(found)).Name);
    }

    [Fact]
    public void VisibleElementsComeFirst()
    {
        var t = Tree();
        t.Ok.IsOffscreen = true;
        var found = ElementFinder.FindAll(Selector.Parse("id=okButton"), [t.Root]);
        Assert.Same(t.Inner, found[0]);
    }

    [Theory]
    [InlineData("button", ControlKind.Button)]
    [InlineData("textbox", ControlKind.Edit)]
    [InlineData("LINK", ControlKind.Hyperlink)]
    public void TypeAliases(string value, ControlKind expected) => Assert.Equal(expected, ElementFinder.ParseKind(value));

    [Theory]
    [InlineData("nope")]
    [InlineData("3")]
    public void UnknownTypeThrows(string value) => Assert.Throws<CliException>(() => ElementFinder.ParseKind(value));

    [Fact]
    public void GeneratesAUniqueIdSelector()
    {
        var t = Tree();
        t.Inner.AutomationId = "innerOk";
        Assert.Equal("id=okButton", SelectorGenerator.Generate(t.Ok, [t.Root]));
    }

    [Fact]
    public void FallsBackToNthWhenIdsAreDuplicated()
    {
        var t = Tree();
        Assert.Equal("id=okButton&&nth=1", SelectorGenerator.Generate(t.Inner, [t.Root]));
    }

    [Fact]
    public void UsesTypeAndNameWithoutAnId()
    {
        var t = Tree();
        Assert.Equal("type=Button&&name=Dup&&nth=1", SelectorGenerator.Generate(t.DupB, [t.Root]));
        var single = new FakeElement(ControlKind.CheckBox, "Agree now");
        t.Root.Add(single);
        Assert.Equal("type=CheckBox&&name=\"Agree now\"", SelectorGenerator.Generate(single, [t.Root]));
    }

    [Fact]
    public void GeneratedSelectorsResolveToTheSameElement()
    {
        var t = Tree();
        foreach (var el in t.Root.Descendants())
        {
            var selector = SelectorGenerator.Generate(el, [t.Root]);
            var found = ElementFinder.FindAll(Selector.Parse(selector), [t.Root]);
            Assert.Same(el, found[0]);
        }
    }

    [Fact]
    public void HitTextIsPromotedToItsButton()
    {
        var text = new FakeElement(ControlKind.Text, "Submit");
        var button = new FakeElement(ControlKind.Button, "Submit", "submitButton").Add(text);
        new FakeElement(ControlKind.Window, "W").Add(button);
        Assert.Same(button, ElementHelpers.PromoteToInteractive(text));
        Assert.Same(button, ElementHelpers.PromoteToInteractive(button));

        var label = new FakeElement(ControlKind.Text, "Label");
        new FakeElement(ControlKind.Window, "W").Add(new FakeElement(ControlKind.Pane).Add(label));
        Assert.Same(label, ElementHelpers.PromoteToInteractive(label));
    }

    [Theory]
    [InlineData("12345678", true)]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301", true)]
    [InlineData("okButton", false)]
    [InlineData("1001", false)]
    public void UnstableAutomationIds(string aid, bool generated) => Assert.Equal(generated, SelectorGenerator.LooksGenerated(aid));
}
