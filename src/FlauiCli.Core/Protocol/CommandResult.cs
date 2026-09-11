using System.Text.Json.Serialization;

namespace FlauiCli.Core.Protocol;

/// <summary>Process exit codes.</summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int AssertionFailed = 1;
    public const int Error = 2;
    public const int DaemonUnavailable = 3;
}

/// <summary>Result of a command.</summary>
public sealed class CommandResult
{
    public bool Ok { get; set; }

    public int ExitCode { get; set; }

    /// <summary>Output for humans / agents (Markdown style).</summary>
    public string Text { get; set; } = "";

    public string? Error { get; set; }

    /// <summary>Extra structured data (for example record status) used by the CLI side.</summary>
    public Dictionary<string, string>? Data { get; set; }

    [JsonIgnore]
    public bool IsAssertionFailure => ExitCode == ExitCodes.AssertionFailed;

    public static CommandResult Success(string text, Dictionary<string, string>? data = null) =>
        new() { Ok = true, ExitCode = ExitCodes.Success, Text = text, Data = data };

    public static CommandResult Failure(string error, int exitCode = ExitCodes.Error, string text = "") =>
        new() { Ok = false, ExitCode = exitCode, Error = error, Text = text };
}
