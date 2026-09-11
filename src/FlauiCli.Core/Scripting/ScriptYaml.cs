using System.Globalization;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Protocol;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace FlauiCli.Core.Scripting;

/// <summary>
/// Reads and writes YAML scripts. Step formats:
/// <code>
/// - click: id=num1Button              # scalar = the command's first argument
/// - fill: { target: id=txt, text: hi } # mapping = named arguments
/// - click: id=ok
///   doc: Press OK                      # doc = description used in generated documents
/// </code>
/// </summary>
public static class ScriptYaml
{
    private static readonly HashSet<string> NoteKeys = new(["doc", "note"], StringComparer.OrdinalIgnoreCase);

    public static ScriptDocument Load(string path)
    {
        if (!File.Exists(path)) throw new CliException($"Script file not found: {path}");
        var doc = Parse(File.ReadAllText(path), path);
        doc.SourcePath = Path.GetFullPath(path);
        return doc;
    }

    public static ScriptDocument Parse(string yaml, string? sourceName = null)
    {
        var where = sourceName ?? "script";
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlException ex) { throw new CliException($"{where}: invalid YAML (line {ex.Start.Line}): {ex.Message}", ex); }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new CliException($"{where}: the top level must be a mapping (name / app / steps)");

        var doc = new ScriptDocument();
        foreach (var (keyNode, value) in root.Children)
        {
            var key = Scalar(keyNode) ?? "";
            switch (key.ToLowerInvariant())
            {
                case "name":
                    doc.Name = Scalar(value);
                    break;
                case "timeout":
                    doc.Timeout = int.TryParse(Scalar(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t)
                        ? t
                        : throw new CliException($"{where}: timeout must be an integer");
                    break;
                case "app":
                    doc.App = ParseApp(value, where);
                    break;
                case "steps":
                    if (value is not YamlSequenceNode seq) throw new CliException($"{where}: steps must be a list");
                    var i = 0;
                    foreach (var step in seq.Children) doc.Steps.Add(ParseStep(step, ++i, where));
                    break;
                default:
                    throw new CliException($"{where}: unknown field {key} (use name, app, timeout, steps)");
            }
        }

        return doc;
    }

    private static AppSpec ParseApp(YamlNode node, string where)
    {
        if (node is YamlScalarNode s) return new AppSpec { Launch = s.Value };
        if (node is not YamlMappingNode m) throw new CliException($"{where}: app must be a mapping");
        var app = new AppSpec();
        foreach (var (k, v) in m.Children)
        {
            var key = Scalar(k) ?? "";
            var value = NodeToString(v, where);
            switch (key.ToLowerInvariant())
            {
                case "launch": app.Launch = value; break;
                case "args": app.Args = value; break;
                case "window": app.Window = value; break;
                case "attach": app.Attach = value; break;
                case "close": app.Close = ParseBool(value, where); break;
                default: throw new CliException($"{where}: unknown app field {key} (use launch, args, window, attach, close)");
            }
        }
        if (app.Launch is null && app.Attach is null && app.Window is null)
            throw new CliException($"{where}: app needs one of launch, attach or window");
        return app;
    }

    private static CommandCall ParseStep(YamlNode node, int index, string where)
    {
        string? command = null;
        YamlNode? body = null;
        string? note = null;

        switch (node)
        {
            case YamlScalarNode s:
                command = s.Value;
                break;
            case YamlMappingNode m:
                foreach (var (k, v) in m.Children)
                {
                    var key = Scalar(k) ?? "";
                    if (NoteKeys.Contains(key)) { note = NodeToString(v, where); continue; }
                    if (command is not null)
                        throw new CliException($"{where} step {index}: only one command per step ({command}, {key})");
                    command = key;
                    body = v;
                }
                break;
            default:
                throw new CliException($"{where} step {index}: invalid format");
        }

        if (string.IsNullOrWhiteSpace(command)) throw new CliException($"{where} step {index}: missing command");
        var spec = CommandCatalog.Find(command) ?? throw new CliException($"{where} step {index}: unknown command {command}");
        if (spec.Location == CommandLocation.Local) throw new CliException($"{where} step {index}: {command} cannot be used in scripts");

        var call = new CommandCall(spec.Name);
        switch (body)
        {
            case null:
                break;
            case YamlScalarNode sc when string.IsNullOrEmpty(sc.Value):
                break;
            case YamlScalarNode or YamlSequenceNode:
                var primary = spec.PrimaryArg ?? throw new CliException($"{where} step {index}: {command} takes no arguments");
                call.Set(primary, NodeToString(body, where));
                break;
            case YamlMappingNode mm:
                foreach (var (k, v) in mm.Children) call.Set(Scalar(k) ?? "", NodeToString(v, where));
                break;
        }

        if (note is not null) call.Set("note", note);
        return call;
    }

    private static string? Scalar(YamlNode node) => (node as YamlScalarNode)?.Value;

    private static string NodeToString(YamlNode node, string where) => node switch
    {
        YamlScalarNode s => s.Value ?? "",
        YamlSequenceNode seq => string.Join(CommandCall.MultiValueSeparator, seq.Children.Select(c => NodeToString(c, where))),
        _ => throw new CliException($"{where}: argument values must be text or a list"),
    };

    private static bool ParseBool(string v, string where) => v.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "1" or "on" => true,
        "false" or "no" or "0" or "off" => false,
        _ => throw new CliException($"{where}: cannot parse boolean {v}"),
    };

    /// <summary>Serializes a script to YAML.</summary>
    public static string Save(ScriptDocument doc)
    {
        var root = new Dictionary<string, object>();
        if (doc.Name is not null) root["name"] = doc.Name;
        if (doc.App is { } app)
        {
            var a = new Dictionary<string, object>();
            if (app.Launch is not null) a["launch"] = app.Launch;
            if (!string.IsNullOrEmpty(app.Args)) a["args"] = app.Args;
            if (app.Attach is not null) a["attach"] = app.Attach;
            if (app.Window is not null) a["window"] = app.Window;
            if (app.Close is not null) a["close"] = app.Close.Value;
            root["app"] = a;
        }
        if (doc.Timeout is not null) root["timeout"] = doc.Timeout.Value;

        var steps = new List<object>();
        foreach (var call in doc.Steps)
        {
            var spec = CommandCatalog.Find(call.Command);
            var args = call.Args.Where(kv => kv.Key != "note").ToList();
            var step = new Dictionary<string, object>();
            object value;
            if (args.Count == 0) value = "";
            else if (args.Count == 1 && spec?.PrimaryArg == args[0].Key) value = ValueOf(args[0].Value);
            else value = args.ToDictionary(kv => kv.Key, kv => ValueOf(kv.Value));

            if (args.Count == 0 && call.Get("note") is null)
            {
                steps.Add(call.Command);
                continue;
            }

            step[call.Command] = value;
            if (call.Get("note") is { } note) step["doc"] = note;
            steps.Add(step);
        }
        root["steps"] = steps;

        var serializer = new SerializerBuilder().WithIndentedSequences().Build();
        return serializer.Serialize(root);
    }

    private static object ValueOf(string v) =>
        v.Contains(CommandCall.MultiValueSeparator) ? v.Split(CommandCall.MultiValueSeparator).ToList() : v;
}
