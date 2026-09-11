using System.Text.Json.Serialization;

namespace FlauiCli.Core.Protocol;

/// <summary>程式結束碼規範。</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int AssertionFailed = 1;
    public const int Error = 2;
    public const int DaemonUnavailable = 3;
}

/// <summary>指令執行結果。</summary>
public sealed class CommandResult
{
    public bool Ok { get; set; }

    public int ExitCode { get; set; }

    /// <summary>給人 / Agent 閱讀的輸出文字（Markdown 風格）。</summary>
    public string Text { get; set; } = "";

    public string? Error { get; set; }

    /// <summary>額外的結構化資料（例如 record status），供 CLI 端判斷用。</summary>
    public Dictionary<string, string>? Data { get; set; }

    [JsonIgnore]
    public bool IsAssertionFailure => ExitCode == ExitCodes.AssertionFailed;

    public static CommandResult Success(string text, Dictionary<string, string>? data = null) =>
        new() { Ok = true, ExitCode = ExitCodes.Success, Text = text, Data = data };

    public static CommandResult Failure(string error, int exitCode = ExitCodes.Error, string text = "") =>
        new() { Ok = false, ExitCode = exitCode, Error = error, Text = text };
}
