using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Scripting;

/// <summary>A YAML test script.</summary>
public sealed class ScriptDocument
{
    public string? Name { get; set; }

    public AppSpec? App { get; set; }

    /// <summary>Default timeout (ms) for every step of this script.</summary>
    public int? Timeout { get; set; }

    /// <summary>Highlight settings for every screenshot of this script; they override the config file.</summary>
    public HighlightSpec? Highlight { get; set; }

    public List<CommandCall> Steps { get; set; } = [];

    /// <summary>Path of the file the script was loaded from.</summary>
    public string? SourcePath { get; set; }

    public string DisplayName =>
        Name ?? (SourcePath is null ? "Untitled script" : Path.GetFileNameWithoutExtension(SourcePath));
}

/// <summary>Script-level highlight settings (see <see cref="CliConfig.HighlightConfig"/>); null fields keep the config file's value.</summary>
public sealed class HighlightSpec
{
    public int? Padding { get; set; }

    public bool? Outline { get; set; }

    public string? Badge { get; set; }

    public void ApplyTo(CliConfig config)
    {
        if (Badge is not null) config.Highlight.Badge = Badge;
        if (Padding is int padding) config.Highlight.Padding = padding;
        if (Outline is bool outline) config.Highlight.Outline = outline;
    }
}

/// <summary>Application to launch / attach to before the script runs.</summary>
public sealed class AppSpec
{
    public string? Launch { get; set; }

    public string? Args { get; set; }

    public string? Window { get; set; }

    /// <summary>Attach to a running process (PID or name).</summary>
    public string? Attach { get; set; }

    /// <summary>Whether to close the application afterwards (default: true for launch, false for attach).</summary>
    public bool? Close { get; set; }
}
