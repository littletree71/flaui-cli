using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Tests;

public class ProtocolTests
{
    [Fact]
    public void CommandCall_JSON往返()
    {
        var call = new CommandCall("fill") { Cwd = @"C:\work" }.Set("target", "id=a").Set("text", "中文 \"引號\"");
        var back = ProtocolJson.DeserializeCall(ProtocolJson.Serialize(call));
        Assert.Equal("fill", back.Command);
        Assert.Equal(@"C:\work", back.Cwd);
        Assert.Equal("中文 \"引號\"", back.Get("text"));
    }

    [Fact]
    public void CommandResult_JSON往返()
    {
        var result = CommandResult.Failure("斷言失敗：x", ExitCodes.AssertionFailed);
        var back = ProtocolJson.DeserializeResult(ProtocolJson.Serialize(result));
        Assert.False(back.Ok);
        Assert.True(back.IsAssertionFailure);
        Assert.Equal("斷言失敗：x", back.Error);
    }

    [Fact]
    public void 參數鍵值正規化()
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
    public void 布林參數(string value, bool expected) =>
        Assert.Equal(expected, new CommandCall("x").Set("flag", value).GetBool("flag"));

    [Fact]
    public void 無效的整數與布林()
    {
        var call = new CommandCall("x").Set("n", "abc").Set("b", "maybe");
        Assert.Throws<CliException>(() => call.GetInt("n"));
        Assert.Throws<CliException>(() => call.GetBool("b"));
        Assert.Throws<CliException>(() => call.Require("missing"));
    }

    [Fact]
    public void 相對路徑以呼叫端工作目錄解析()
    {
        var call = new CommandCall("x") { Cwd = @"C:\work" };
        Assert.Equal(@"C:\work\out\a.png", call.ResolvePath(@"out\a.png"));
        Assert.Equal(@"D:\abs.png", call.ResolvePath(@"D:\abs.png"));
    }
}
