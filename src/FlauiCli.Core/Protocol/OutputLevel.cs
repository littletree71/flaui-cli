namespace FlauiCli.Core.Protocol;

/// <summary>
/// How much a command prints (<c>--log-level</c>, <c>logLevel</c> in the config file). The result itself is always
/// printed; the levels add the optional sections after it. <c>--json</c> data is not affected.
/// </summary>
public enum OutputLevel
{
    /// <summary>The result and errors only.</summary>
    Error,

    /// <summary>Also <c>### Warnings</c>.</summary>
    Warn,

    /// <summary>Also the window and snapshot link added after actions (the default).</summary>
    Info,

    /// <summary>Also <c>### Timing</c> (what <c>--verbose</c> adds).</summary>
    Debug,
}

public static class OutputLevels
{
    public const string Names = "error, warn, info or debug";

    /// <summary>Parses a level name; null means the default (info).</summary>
    public static OutputLevel Parse(string? value) => (value ?? "info").Trim().ToLowerInvariant() switch
    {
        "error" => OutputLevel.Error,
        "warn" or "warning" => OutputLevel.Warn,
        "info" => OutputLevel.Info,
        "debug" => OutputLevel.Debug,
        _ => throw new CliException($"Unknown log level: {value} (use {Names})"),
    };
}
