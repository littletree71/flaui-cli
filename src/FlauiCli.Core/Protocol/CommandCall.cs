using System.Globalization;
using System.Text.Json.Serialization;

namespace FlauiCli.Core.Protocol;

/// <summary>
/// 一次指令呼叫。CLI、Daemon、YAML 腳本與錄製器都使用這個統一模型：
/// 指令名稱 + 以名稱為鍵的參數字典（值一律為字串，多值以換行分隔）。
/// </summary>
public sealed class CommandCall
{
    /// <summary>多值參數（例如多個 --highlight）的分隔字元。</summary>
    public const char MultiValueSeparator = '\n';

    public CommandCall() { }

    public CommandCall(string command, IDictionary<string, string>? args = null)
    {
        Command = command;
        if (args is not null)
        {
            foreach (var (k, v) in args) Set(k, v);
        }
    }

    public string Command { get; set; } = "";

    public Dictionary<string, string> Args { get; set; } = new();

    /// <summary>呼叫端的工作目錄；相對路徑一律以此為基準解析。</summary>
    public string? Cwd { get; set; }

    [JsonIgnore]
    public string? this[string key] => Get(key);

    public CommandCall Set(string key, string? value)
    {
        if (value is not null) Args[Normalize(key)] = value;
        return this;
    }

    public CommandCall Remove(string key)
    {
        Args.Remove(Normalize(key));
        return this;
    }

    public bool Has(string key) => Args.ContainsKey(Normalize(key));

    public string? Get(string key) => Args.TryGetValue(Normalize(key), out var v) ? v : null;

    public string Require(string key)
    {
        var v = Get(key);
        if (string.IsNullOrEmpty(v))
            throw new CliException($"指令 {Command} 缺少必要參數 <{key}>");
        return v;
    }

    public IReadOnlyList<string> GetList(string key)
    {
        var v = Get(key);
        return string.IsNullOrEmpty(v)
            ? []
            : v.Split(MultiValueSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public int? GetInt(string key)
    {
        var v = Get(key);
        if (string.IsNullOrWhiteSpace(v)) return null;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : throw new CliException($"參數 {key} 必須是整數，收到：{v}");
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var v = Get(key);
        if (v is null) return defaultValue;
        return v.Trim().ToLowerInvariant() switch
        {
            "" or "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new CliException($"參數 {key} 必須是布林值，收到：{v}"),
        };
    }

    public CommandCall Clone() => new(Command, Args) { Cwd = Cwd };

    /// <summary>把相對路徑以呼叫端工作目錄解析成絕對路徑。</summary>
    public string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Cwd ?? Environment.CurrentDirectory, path));

    private static string Normalize(string key) => key.Trim().TrimStart('-').ToLowerInvariant();

    public override string ToString() =>
        Command + " " + string.Join(" ", Args.Select(kv => $"{kv.Key}={kv.Value.Replace(MultiValueSeparator, ',')}"));
}
