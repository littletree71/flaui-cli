using FlauiCli.Core.Engine;

namespace FlauiCli.Core.Tests;

public class KeyParserTests
{
    [Theory]
    [InlineData("Enter", new ushort[] { 0x0D })]
    [InlineData("ctrl+a", new ushort[] { 0x11, 0x41 })]
    [InlineData("Ctrl+Shift+S", new ushort[] { 0x11, 0x10, 0x53 })]
    [InlineData("Alt+F4", new ushort[] { 0x12, 0x73 })]
    [InlineData("Esc", new ushort[] { 0x1B })]
    [InlineData("5", new ushort[] { 0x35 })]
    [InlineData("F12", new ushort[] { 0x7B })]
    public void Parse(string keys, ushort[] expected) => Assert.Equal(expected, KeyParser.Parse(keys));

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+NoSuchKey")]
    public void InvalidKeysThrow(string keys) => Assert.Throws<CliException>(() => KeyParser.Parse(keys));

    [Fact]
    public void FormatUsesCanonicalNames()
    {
        Assert.Equal("Ctrl+Shift+S", KeyParser.Format([0x11, 0x10, 0x53]));
        Assert.Equal("Enter", KeyParser.Format(0x0D));
        Assert.Equal("Ctrl", KeyParser.Format(0xA2));
    }

    [Fact]
    public void FormatIsTheInverseOfParse()
    {
        foreach (var keys in new[] { "Ctrl+A", "Alt+F4", "Shift+Tab", "Enter", "PageDown" })
            Assert.Equal(keys, KeyParser.Format(KeyParser.Parse(keys)));
    }

    [Fact]
    public void Modifiers()
    {
        Assert.True(KeyParser.IsModifier(0x11));
        Assert.True(KeyParser.IsModifier(0xA0));
        Assert.False(KeyParser.IsModifier(0x41));
    }
}
