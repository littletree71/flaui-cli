using System.Globalization;
using FlauiCli.Core.Commands;
using FlauiCli.Core.Protocol;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace FlauiCli.Core.Scripting;

/// <summary>
/// YAML 腳本的讀寫。步驟格式：
/// <code>
/// - click: id=num1Button              # 純量 = 指令的第一個參數
/// - fill: { target: id=txt, text: hi } # mapping = 具名參數
/// - click: id=ok
///   doc: 按下確定                       # doc = 操作文件的說明
/// </code>
/// </summary>
public static class ScriptYaml
{
    private static readonly HashSet<string> NoteKeys = new(["doc", "note"], StringComparer.OrdinalIgnoreCase);

    public static ScriptDocument Load(string path)
    {
        if (!File.Exists(path)) throw new CliException($"找不到腳本檔：{path}");
        var doc = Parse(File.ReadAllText(path), path);
        doc.SourcePath = Path.GetFullPath(path);
        return doc;
    }

    public static ScriptDocument Parse(string yaml, string? sourceName = null)
    {
        var where = sourceName ?? "腳本";
        var stream = new YamlStream();
        try { stream.Load(new StringReader(yaml)); }
        catch (YamlException ex) { throw new CliException($"{where} YAML 格式錯誤（第 {ex.Start.Line} 行）：{ex.Message}", ex); }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
            throw new CliException($"{where} 最上層必須是 mapping（name / app / steps）");

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
                        : throw new CliException($"{where}：timeout 必須是整數");
                    break;
                case "app":
                    doc.App = ParseApp(value, where);
                    break;
                case "steps":
                    if (value is not YamlSequenceNode seq) throw new CliException($"{where}：steps 必須是清單");
                    var i = 0;
                    foreach (var step in seq.Children) doc.Steps.Add(ParseStep(step, ++i, where));
                    break;
                default:
                    throw new CliException($"{where}：未知的欄位 {key}（可用：name, app, timeout, steps）");
            }
        }

        return doc;
    }

    private static AppSpec ParseApp(YamlNode node, string where)
    {
        if (node is YamlScalarNode s) return new AppSpec { Launch = s.Value };
        if (node is not YamlMappingNode m) throw new CliException($"{where}：app 必須是 mapping");
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
                default: throw new CliException($"{where}：app 未知的欄位 {key}（可用：launch, args, window, attach, close）");
            }
        }
        if (app.Launch is null && app.Attach is null && app.Window is null)
            throw new CliException($"{where}：app 需要 launch、attach 或 window 其中之一");
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
                        throw new CliException($"{where} 第 {index} 步：一個步驟只能有一個指令（{command}、{key}）");
                    command = key;
                    body = v;
                }
                break;
            default:
                throw new CliException($"{where} 第 {index} 步：格式錯誤");
        }

        if (string.IsNullOrWhiteSpace(command)) throw new CliException($"{where} 第 {index} 步：缺少指令");
        var spec = CommandCatalog.Find(command) ?? throw new CliException($"{where} 第 {index} 步：未知的指令 {command}");
        if (spec.Location == CommandLocation.Local) throw new CliException($"{where} 第 {index} 步：指令 {command} 不能用在腳本中");

        var call = new CommandCall(spec.Name);
        switch (body)
        {
            case null:
                break;
            case YamlScalarNode sc when string.IsNullOrEmpty(sc.Value):
                break;
            case YamlScalarNode or YamlSequenceNode:
                var primary = spec.PrimaryArg ?? throw new CliException($"{where} 第 {index} 步：{command} 不接受參數");
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
        _ => throw new CliException($"{where}：參數值必須是文字或清單"),
    };

    private static bool ParseBool(string v, string where) => v.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "1" or "on" => true,
        "false" or "no" or "0" or "off" => false,
        _ => throw new CliException($"{where}：無法解析布林值 {v}"),
    };

    /// <summary>把腳本序列化成 YAML。</summary>
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
