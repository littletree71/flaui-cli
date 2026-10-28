using System.Text.Json;
using FlauiCli.Core.Protocol;
using System.Text.Json.Serialization;

namespace FlauiCli.Core;

/// <summary>
/// Configuration file <c>.flaui-cli/config.json</c> (relative to the caller's working directory).
/// </summary>
public sealed class CliConfig
{
    public const string DefaultDirName = ".flaui-cli";

    public TimeoutConfig Timeouts { get; set; } = new();

    /// <summary>Output directory for snapshots, screenshots and so on, relative to the working directory.</summary>
    public string OutputDir { get; set; } = DefaultDirName;

    /// <summary>Whether action commands automatically save a snapshot file afterwards.</summary>
    public bool AutoSnapshot { get; set; } = true;

    public SnapshotConfig Snapshot { get; set; } = new();

    public HighlightConfig Highlight { get; set; } = new();

    /// <summary>How much commands print: error, warn, info (default) or debug (see <see cref="OutputLevel"/>).</summary>
    public string LogLevel { get; set; } = "info";

    /// <summary>Minutes of inactivity after which the daemon exits.</summary>
    public int DaemonIdleMinutes { get; set; } = 30;

    public sealed class TimeoutConfig
    {
        /// <summary>Default timeout (ms) for finding elements and auto-waiting.</summary>
        public int Action { get; set; } = 5000;

        /// <summary>Timeout (ms) for launching an application and waiting for its window.</summary>
        public int Launch { get; set; } = 20000;

        /// <summary>
        /// Timeout (ms) a command that needs mouse / keyboard input or a screenshot waits for a locked workstation to be
        /// unlocked; 0 fails at once (exit code 4).
        /// </summary>
        public int Unlock { get; set; }
    }

    public sealed class SnapshotConfig
    {
        /// <summary>Maximum depth; 0 means unlimited.</summary>
        public int Depth { get; set; }

        /// <summary>Whether off-screen (IsOffscreen) elements are included.</summary>
        public bool IncludeOffscreen { get; set; }
    }

    public sealed class HighlightConfig
    {
        /// <summary>
        /// Pixels between an element and its highlight outline: positive draws the outline outside the element, 0 on
        /// its edge, negative inside it (keeps the outlines of touching elements apart).
        /// </summary>
        public int Padding { get; set; } = 3;

        /// <summary>Whether highlighted elements get a red outline; when false only the number badges are drawn.</summary>
        public bool Outline { get; set; } = true;

        /// <summary>
        /// Where number badges go: auto (chosen per element), top, bottom, left, right, top-left, top-right, bottom-left,
        /// bottom-right or inside.
        /// </summary>
        public string Badge { get; set; } = "auto";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Loads the configuration from the working directory; returns defaults when the file does not exist.</summary>
    public static CliConfig Load(string? cwd)
    {
        var path = Path.Combine(cwd ?? Environment.CurrentDirectory, DefaultDirName, "config.json");
        if (!File.Exists(path)) return new CliConfig();
        try
        {
            return JsonSerializer.Deserialize<CliConfig>(File.ReadAllText(path), JsonOptions) ?? new CliConfig();
        }
        catch (JsonException ex)
        {
            throw new CliException($"Invalid configuration file {path}: {ex.Message}", ex);
        }
    }

    public string ResolveOutputDir(string? cwd)
    {
        var dir = Path.IsPathRooted(OutputDir) ? OutputDir : Path.Combine(cwd ?? Environment.CurrentDirectory, OutputDir);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
