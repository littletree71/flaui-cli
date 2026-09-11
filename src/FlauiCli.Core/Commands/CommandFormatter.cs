using System.Text;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Commands;

/// <summary>Turns a <see cref="CommandCall"/> back into a CLI command line (shown in reports and documents).</summary>
public static class CommandFormatter
{
    public static string ToCli(CommandCall call)
    {
        var sb = new StringBuilder(call.Command);
        var spec = CommandCatalog.Find(call.Command);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (spec is not null)
        {
            foreach (var a in spec.Args)
            {
                if (call.Get(a.Name) is not { } v) continue;
                used.Add(a.Name);
                foreach (var item in a.Variadic ? call.GetList(a.Name) : [v]) sb.Append(' ').Append(QuoteArg(item));
            }
            foreach (var o in spec.Options)
            {
                if (call.Get(o.Name) is not { } v) continue;
                used.Add(o.Name);
                if (o.Flag)
                {
                    if (call.GetBool(o.Name)) sb.Append(" --").Append(o.Name);
                    continue;
                }
                foreach (var item in o.Multiple ? call.GetList(o.Name) : [v]) sb.Append(" --").Append(o.Name).Append(' ').Append(QuoteArg(item));
            }
        }

        foreach (var (k, v) in call.Args.Where(kv => !used.Contains(kv.Key)))
            sb.Append(" --").Append(k).Append(' ').Append(QuoteArg(v));

        return sb.ToString();
    }

    public static string QuoteArg(string s) =>
        s.Length == 0 || s.Any(c => char.IsWhiteSpace(c) || c is '"' or '&' or '|' or '<' or '>' or '^')
            ? "\"" + s.Replace("\"", "\\\"") + "\""
            : s;
}
