using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlauiCli.Core;

/// <summary>
/// 設定檔 <c>.flaui-cli/config.json</c>（相對於呼叫端工作目錄）。
/// </summary>
public sealed class CliConfig
{
    public const string DefaultDirName = ".flaui-cli";

    public TimeoutConfig Timeouts { get; set; } = new();

    /// <summary>輸出目錄（snapshot、截圖等），相對於工作目錄。</summary>
    public string OutputDir { get; set; } = DefaultDirName;

    /// <summary>動作指令完成後是否自動存一份 snapshot 檔。</summary>
    public bool AutoSnapshot { get; set; } = true;

    public SnapshotConfig Snapshot { get; set; } = new();

    /// <summary>Daemon 閒置多久（分鐘）後自動結束。</summary>
    public int DaemonIdleMinutes { get; set; } = 30;

    public sealed class TimeoutConfig
    {
        /// <summary>尋找元素 / 自動等待的預設逾時（毫秒）。</summary>
        public int Action { get; set; } = 5000;

        /// <summary>啟動應用程式並等待視窗的逾時（毫秒）。</summary>
        public int Launch { get; set; } = 20000;
    }

    public sealed class SnapshotConfig
    {
        /// <summary>最大深度，0 表示不限。</summary>
        public int Depth { get; set; }

        /// <summary>是否包含畫面外（IsOffscreen）的元素。</summary>
        public bool IncludeOffscreen { get; set; }
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

    /// <summary>從工作目錄載入設定；檔案不存在時回傳預設值。</summary>
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
            throw new CliException($"設定檔格式錯誤：{path}：{ex.Message}", ex);
        }
    }

    public string ResolveOutputDir(string? cwd)
    {
        var dir = Path.IsPathRooted(OutputDir) ? OutputDir : Path.Combine(cwd ?? Environment.CurrentDirectory, OutputDir);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
