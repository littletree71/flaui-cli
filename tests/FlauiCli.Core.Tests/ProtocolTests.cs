using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void CommandCallJsonRoundTrip()
    {
        // Non-ASCII text and quotes must survive the pipe protocol
        var call = new CommandCall("fill") { Cwd = @"C:\work" }.Set("target", "id=a").Set("text", "héllo 中文 \"quotes\"");
        var back = ProtocolJson.DeserializeCall(ProtocolJson.Serialize(call));
        Assert.Equal("fill", back.Command);
        Assert.Equal(@"C:\work", back.Cwd);
        Assert.Equal("héllo 中文 \"quotes\"", back.Get("text"));
    }

    [Fact]
    public void CommandResultJsonRoundTrip()
    {
        var result = CommandResult.Failure("Assertion failed: x", ExitCodes.AssertionFailed);
        var back = ProtocolJson.DeserializeResult(ProtocolJson.Serialize(result));
        Assert.False(back.Ok);
        Assert.True(back.IsAssertionFailure);
        Assert.Equal("Assertion failed: x", back.Error);
    }

    [Theory]
    [InlineData("fill")]
    [InlineData("type")]
    public void RedactedMasksTypedTextForTheDaemonLog(string command)
    {
        var call = new CommandCall(command).Set("target", "id=pwd").Set("text", "S3cret!");
        var log = call.Redacted().ToString();
        Assert.DoesNotContain("S3cret!", log);
        Assert.Contains(CommandCall.Masked, log);
        Assert.Equal("S3cret!", call.Get("text")); // the original is untouched
        Assert.Contains("S3cret!", new CommandCall("assert").Set("text", "S3cret!").Redacted().ToString());
    }

    [Fact]
    public void ArgumentKeysAreNormalized()
    {
        var call = new CommandCall("x").Set("--Timeout", "100");
        Assert.Equal(100, call.GetInt("timeout"));
        Assert.True(call.Has("TIMEOUT"));
        call.Remove("timeout");
        Assert.False(call.Has("timeout"));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("", true)]
    [InlineData("no", false)]
    [InlineData("0", false)]
    public void BooleanArguments(string value, bool expected) =>
        Assert.Equal(expected, new CommandCall("x").Set("flag", value).GetBool("flag"));

    [Fact]
    public void InvalidIntegersAndBooleansThrow()
    {
        var call = new CommandCall("x").Set("n", "abc").Set("b", "maybe");
        Assert.Throws<CliException>(() => call.GetInt("n"));
        Assert.Throws<CliException>(() => call.GetBool("b"));
        Assert.Throws<CliException>(() => call.Require("missing"));
    }

    [Fact]
    public void RelativePathsResolveAgainstTheCallersDirectory()
    {
        var call = new CommandCall("x") { Cwd = @"C:\work" };
        Assert.Equal(@"C:\work\out\a.png", call.ResolvePath(@"out\a.png"));
        Assert.Equal(@"D:\abs.png", call.ResolvePath(@"D:\abs.png"));
    }
}
