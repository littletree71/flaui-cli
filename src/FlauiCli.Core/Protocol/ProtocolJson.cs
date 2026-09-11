using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlauiCli.Core.Daemon;

namespace FlauiCli.Core.Protocol;

/// <summary>Named Pipe 通訊與 session 檔使用的 JSON 序列化設定（source generator）。</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CommandCall))]
[JsonSerializable(typeof(CommandResult))]
[JsonSerializable(typeof(SessionInfo))]
internal partial class ProtocolJsonContext : JsonSerializerContext;

public static class ProtocolJson
{
    private static readonly ProtocolJsonContext Context = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private static readonly ProtocolJsonContext IndentedContext = new(new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    });

    public static string Serialize(CommandCall call) => JsonSerializer.Serialize(call, Context.CommandCall);

    public static string Serialize(CommandResult result, bool indented = false) =>
        JsonSerializer.Serialize(result, indented ? IndentedContext.CommandResult : Context.CommandResult);

    public static string Serialize(SessionInfo info) => JsonSerializer.Serialize(info, IndentedContext.SessionInfo);

    public static CommandCall DeserializeCall(string json) =>
        JsonSerializer.Deserialize(json, Context.CommandCall) ?? throw new CliException("無法解析指令 JSON");

    public static CommandResult DeserializeResult(string json) =>
        JsonSerializer.Deserialize(json, Context.CommandResult) ?? throw new CliException("無法解析回應 JSON");

    public static SessionInfo? DeserializeSession(string json)
    {
        try { return JsonSerializer.Deserialize(json, Context.SessionInfo); }
        catch (JsonException) { return null; }
    }
}
