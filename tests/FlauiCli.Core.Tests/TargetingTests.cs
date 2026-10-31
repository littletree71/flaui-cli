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
    public void RegexMatchesNamesWithSurroundingSpaces()
    {
        // DataGridView check box cells without header text are named " Row 0": a leading space
        var cell0 = new FakeElement(ControlKind.DataItem, " Row 0");
        var cell1 = new FakeElement(ControlKind.DataItem, " Row 1");
        var named = new FakeElement(ControlKind.DataItem, "Name Row 0");
        var root = new FakeElement(ControlKind.Window, "W").Add(cell0, cell1, named);

        Assert.Equal([cell0], ElementFinder.FindAll(Selector.Parse(@"type=dataitem&&regex=""^\s*row 0$"""), [root]));
        Assert.Equal([cell0], ElementFinder.FindAll(Selector.Parse("type=dataitem&&name=\" Row 0\""), [root]));
        Assert.Equal([cell0, named], ElementFinder.FindAll(Selector.Parse("text=Row 0"), [root]));
        Assert.Empty(ElementFinder.FindAll(Selector.Parse("type=dataitem&&name=Row 0"), [root]));
    }

    [Fact]
    public void AVisibleMatchComesFirstUnlessThereIsNoTimeOrRowsWereAskedFor()
    {
        var hidden = new FakeElement(ControlKind.DataItem, "Cell") { IsOffscreen = true };
        var shown = new FakeElement(ControlKind.DataItem, "Cell");
        var root = new FakeElement(ControlKind.Window, "W").Add(hidden, shown);
        var selector = Selector.Parse("type=dataitem");

        using (SearchBudget.Begin(5000))
        {
            Assert.Same(shown, ElementFinder.FindFirst(selector, [root]));
            Assert.Equal([shown, hidden], ElementFinder.FindAll(selector, [root]));

            // Rows asked for: the first match is the answer, whether it is visible or not
            SearchBudget.LimitRows(5000, 0);
            Assert.Same(hidden, ElementFinder.FindFirst(selector, [root]));
            Assert.Equal([hidden, shown], ElementFinder.FindAll(selector, [root]));
        }

        // The same for a column asked for
        using (SearchBudget.Begin(5000))
        {
            SearchBudget.Column = "Amount";
            Assert.Same(hidden, ElementFinder.FindFirst(selector, [root]));
        }

        // The search did not finish, or the time is up: no more cross-process reads to sort the result
        using (SearchBudget.Begin(5000))
        {
            SearchBudget.ReportIncomplete("stopped");
            Assert.Equal([hidden, shown], ElementFinder.FindAll(selector, [root]));
        }
        using (SearchBudget.Begin(0)) Assert.Equal([hidden, shown], ElementFinder.FindAll(selector, [root]));
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

    [Fact]
    public void FindFirstStopsAtTheFirstVisibleMatch()
    {
        var t = Tree();
        Assert.Same(t.Ok, ElementFinder.FindFirst(Selector.Parse("id=okButton"), [t.Root]));
        Assert.Equal(1, t.Root.FindFirstCalls);
        Assert.Same(t.Inner, ElementFinder.FindFirst(Selector.Parse("id=panel >> id=okButton"), [t.Root]));
        Assert.Null(ElementFinder.FindFirst(Selector.Parse("id=missing"), [t.Root]));
    }

    [Theory]
    [InlineData("id=okButton")]
    [InlineData("name=Dup")]
    [InlineData("name=Dup&&nth=1")]
    [InlineData("text=pan")]
    [InlineData("type=Button&&name=OK")]
    [InlineData("id=top >> name=Dup")]
    [InlineData("id=nothing")]
    public void FindFirstAgreesWithFindAll(string selector)
    {
        foreach (var hide in new[] { "", "Ok", "DupA", "all" })
        {
            var t = Tree();
            if (hide is "Ok" or "all") t.Ok.IsOffscreen = true;
            if (hide is "DupA" or "all") t.DupA.IsOffscreen = true;
            if (hide is "all") { t.Inner.IsOffscreen = true; t.DupB.IsOffscreen = true; }
            var all = ElementFinder.FindAll(Selector.Parse(selector), [t.Root]);
            Assert.Same(all.FirstOrDefault(), ElementFinder.FindFirst(Selector.Parse(selector), [t.Root]));
        }
    }

    [Fact]
    public void FindFirstPrefersAVisibleMatchInALaterRoot()
    {
        var t = Tree();
        t.Ok.IsOffscreen = true;
        t.Inner.IsOffscreen = true;
        var popup = new FakeElement(ControlKind.Window, "Popup").Add(new FakeElement(ControlKind.Button, "OK", "okButton"));
        Assert.Same(popup.Children[0], ElementFinder.FindFirst(Selector.Parse("id=okButton"), [t.Root, popup]));
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
