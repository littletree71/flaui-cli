using FlauiCli.Core.Targeting;

namespace FlauiCli.Core.Tests;

public class SelectorTests
{
    [Theory]
    [InlineData("e1")]
    [InlineData("e123")]
    [InlineData("  e7 ")]
    public void Ref_被辨識(string raw)
    {
        var s = Selector.Parse(raw);
        Assert.True(s.IsRef);
        Assert.Equal(raw.Trim(), s.Ref);
    }

    [Fact]
    public void Id_條件()
    {
        var s = Selector.Parse("id=num1Button");
        var part = Assert.Single(s.Parts);
        Assert.Equal(new SelectorCondition("id", "num1Button"), Assert.Single(part.Conditions));
    }

    [Fact]
    public void 組合條件與nth()
    {
        var part = Assert.Single(Selector.Parse("type=Button&&name=\"Save as\"&&nth=2").Parts);
        Assert.Equal(2, part.Conditions.Count);
        Assert.Equal("Save as", part.Conditions[1].Value);
        Assert.Equal(2, part.Nth);
    }

    [Fact]
    public void 階層選擇器()
    {
        var s = Selector.Parse("id=panel >> name=OK");
        Assert.Equal(2, s.Parts.Count);
        Assert.Equal("OK", s.Parts[1].Conditions[0].Value);
    }

    [Fact]
    public void 引號內的分隔符號不被切開()
    {
        var part = Assert.Single(Selector.Parse("name=\"A && B >> C\"").Parts);
        Assert.Equal("A && B >> C", Assert.Single(part.Conditions).Value);
    }

    [Fact]
    public void 跳脫的引號()
    {
        var part = Assert.Single(Selector.Parse("name=\"say \\\"hi\\\"\"").Parts);
        Assert.Equal("say \"hi\"", part.Conditions[0].Value);
    }

    [Fact]
    public void XPath_整段保留()
    {
        var part = Assert.Single(Selector.Parse("xpath=//Button[@Name='a' and @AutomationId='b']").Parts);
        Assert.Equal("//Button[@Name='a' and @AutomationId='b']", part.XPath);
    }

    [Fact]
    public void 沒有前綴視為名稱()
    {
        var part = Assert.Single(Selector.Parse("Display is 0").Parts);
        Assert.Equal(new SelectorCondition("name", "Display is 0"), part.Conditions[0]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nth=-1")]
    [InlineData("nth=abc")]
    public void 無效選擇器擲出例外(string raw) => Assert.Throws<CliException>(() => Selector.Parse(raw));

    [Theory]
    [InlineData("id=a", "id=a")]
    [InlineData("type=Button && name=\"Save as\"", "type=Button&&name=\"Save as\"")]
    [InlineData("id=p >> id=c&&nth=1", "id=p >> id=c&&nth=1")]
    public void ToString_可重新解析(string raw, string expected)
    {
        var s = Selector.Parse(raw);
        Assert.Equal(expected, s.ToString());
        Assert.Equal(expected, Selector.Parse(s.ToString()).ToString());
    }

    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("a b", "\"a b\"")]
    [InlineData("", "\"\"")]
    [InlineData("x=y", "\"x=y\"")]
    public void Quote(string value, string expected) => Assert.Equal(expected, Selector.Quote(value));
}
