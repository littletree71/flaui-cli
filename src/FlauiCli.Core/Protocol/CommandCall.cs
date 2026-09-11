using System.Globalization;
using System.Text.Json.Serialization;

namespace FlauiCli.Core.Protocol;

/// <summary>
/// A single command invocation. The CLI, the daemon, YAML scripts and the recorder all share this
/// model: a command name plus a dictionary of named arguments (values are always strings; multiple
/// values are separated by new lines).
/// </summary>
public sealed class CommandCall
{
    /// <summary>Separator for multi-valued arguments (for example several --highlight options).</summary>
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

    /// <summary>Working directory of the caller; relative paths are resolved against it.</summary>
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
            throw new CliException($"Command '{Command}' is missing the required argument <{key}>");
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
            : throw new CliException($"Argument '{key}' must be an integer, got: {v}");
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var v = Get(key);
        if (v is null) return defaultValue;
        return v.Trim().ToLowerInvariant() switch
        {
            "" or "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new CliException($"Argument '{key}' must be a boolean, got: {v}"),
        };
    }

    public CommandCall Clone() => new(Command, Args) { Cwd = Cwd };

    /// <summary>Placeholder written instead of a secret value (passwords) in logs, scripts and documents.</summary>
    public const string Masked = "********";

    /// <summary>
    /// A copy safe for persistent logs: the text typed by <c>fill</c> / <c>type</c> is masked because the
    /// target may be a password field (which is only known once the element has been resolved).
    /// </summary>
    public CommandCall Redacted()
    {
        var copy = Clone();
        if (Command is "fill" or "type" && copy.Has("text")) copy.Set("text", Masked);
        return copy;
    }

    /// <summary>Resolves a relative path against the caller's working directory.</summary>
    public string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Cwd ?? Environment.CurrentDirectory, path));

    private static string Normalize(string key) => key.Trim().TrimStart('-').ToLowerInvariant();

    public override string ToString() =>
        Command + " " + string.Join(" ", Args.Select(kv => $"{kv.Key}={kv.Value.Replace(MultiValueSeparator, ',')}"));
}
