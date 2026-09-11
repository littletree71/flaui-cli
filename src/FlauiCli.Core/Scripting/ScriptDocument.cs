using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Scripting;

/// <summary>A YAML test script.</summary>
public sealed class ScriptDocument
{
    public string? Name { get; set; }

    public AppSpec? App { get; set; }

    /// <summary>Default timeout (ms) for every step of this script.</summary>
    public int? Timeout { get; set; }

    public List<CommandCall> Steps { get; set; } = [];

    /// <summary>Path of the file the script was loaded from.</summary>
    public string? SourcePath { get; set; }

    public string DisplayName =>
        Name ?? (SourcePath is null ? "Untitled script" : Path.GetFileNameWithoutExtension(SourcePath));
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
