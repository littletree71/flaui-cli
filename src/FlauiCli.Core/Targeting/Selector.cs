using System.Text;
using System.Text.RegularExpressions;

namespace FlauiCli.Core.Targeting;

/// <summary>A single condition, for example id=num1Button.</summary>
public sealed record SelectorCondition(string Key, string Value);

/// <summary>A group of conditions joined with &amp;&amp;; several parts joined with &gt;&gt; mean "inside the previous one".</summary>
public sealed class SelectorPart
{
    public List<SelectorCondition> Conditions { get; } = [];

    /// <summary>Which match to take (zero-based).</summary>
    public int? Nth { get; set; }

    public string? XPath { get; set; }

    public override string ToString()
    {
        if (XPath is not null) return "xpath=" + XPath;
        var items = Conditions.Select(c => c.Key + "=" + Selector.Quote(c.Value)).ToList();
        if (Nth is not null) items.Add("nth=" + Nth);
        return string.Join("&&", items);
    }
}

/// <summary>
/// Element locator string. Supports:
/// <list type="bullet">
/// <item>ref: <c>e12</c> (from a snapshot)</item>
/// <item><c>id=</c> AutomationId, <c>name=</c> exact name, <c>text=</c> name contains, <c>type=</c> ControlType, <c>class=</c> ClassName, <c>nth=</c> index</item>
/// <item><c>xpath=</c> FlaUI XPath</item>
/// <item><c>&amp;&amp;</c> to combine conditions, <c>&gt;&gt;</c> for nesting</item>
/// <item>a string without a known prefix is treated as a name</item>
/// </list>
/// </summary>
public sealed partial class Selector
{
    public static readonly string[] KnownKeys = ["id", "name", "text", "type", "class", "nth", "xpath"];

    private Selector(string raw) => Raw = raw;

    public string Raw { get; }

    /// <summary>Set when the selector is a snapshot ref (e12).</summary>
    public string? Ref { get; private init; }

    public List<SelectorPart> Parts { get; } = [];

    public bool IsRef => Ref is not null;

    [GeneratedRegex(@"^e\d+$")]
    private static partial Regex RefPattern();

    public static bool LooksLikeRef(string s) => RefPattern().IsMatch(s.Trim());

    public static Selector Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new CliException("The selector must not be empty");
        var text = raw.Trim();
        if (LooksLikeRef(text)) return new Selector(text) { Ref = text };

        var selector = new Selector(text);
        foreach (var partText in SplitOutsideQuotes(text, ">>"))
        {
            var part = new SelectorPart();
            var trimmed = partText.Trim();
            if (trimmed.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
            {
                // An XPath may contain &&, so the whole part is taken as XPath
                part.XPath = Unquote(trimmed[6..].Trim());
                selector.Parts.Add(part);
                continue;
            }

            foreach (var condText in SplitOutsideQuotes(trimmed, "&&"))
            {
                var c = condText.Trim();
                if (c.Length == 0) continue;
                var eq = IndexOutsideQuotes(c, '=');
                var key = eq > 0 ? c[..eq].Trim().ToLowerInvariant() : "";
                if (eq > 0 && KnownKeys.Contains(key))
                {
                    var value = Unquote(c[(eq + 1)..].Trim());
                    switch (key)
                    {
                        case "nth":
                            part.Nth = int.TryParse(value, out var n) && n >= 0
                                ? n
                                : throw new CliException($"nth must be a non-negative integer: {value}");
                            break;
                        case "xpath":
                            part.XPath = value;
                            break;
                        default:
                            part.Conditions.Add(new SelectorCondition(key, value));
                            break;
                    }
                }
                else
                {
                    // No known prefix: exact name match
                    part.Conditions.Add(new SelectorCondition("name", Unquote(c)));
                }
            }

            if (part.Conditions.Count == 0 && part.XPath is null)
                throw new CliException($"Invalid selector: {raw}");
            selector.Parts.Add(part);
        }

        return selector;
    }

    /// <summary>Quotes a value when needed (whitespace, &amp;&amp;, &gt;&gt;, quotes or =).</summary>
    public static string Quote(string value)
    {
        var needs = value.Length == 0 || value.Any(char.IsWhiteSpace) || value.Contains("&&") || value.Contains(">>")
                    || value.Contains('"') || value.Contains('\'') || value.Contains('=');
        return needs ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : value;
    }

    public override string ToString() => Ref ?? string.Join(" >> ", Parts.Select(p => p.ToString()));

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && (s[0] == '"' && s[^1] == '"' || s[0] == '\'' && s[^1] == '\''))
        {
            var inner = s[1..^1];
            var sb = new StringBuilder(inner.Length);
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '\\' && i + 1 < inner.Length && (inner[i + 1] == '"' || inner[i + 1] == '\'' || inner[i + 1] == '\\'))
                {
                    sb.Append(inner[++i]);
                    continue;
                }
                sb.Append(inner[i]);
            }
            return sb.ToString();
        }
        return s;
    }

    private static int IndexOutsideQuotes(string s, char target)
    {
        char quote = '\0';
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (quote != '\0')
            {
                if (ch == '\\') { i++; continue; }
                if (ch == quote) quote = '\0';
            }
            else if (ch is '"' or '\'') quote = ch;
            else if (ch == target) return i;
        }
        return -1;
    }

    private static List<string> SplitOutsideQuotes(string s, string separator)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        char quote = '\0';
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (quote != '\0')
            {
                sb.Append(ch);
                if (ch == '\\' && i + 1 < s.Length) { sb.Append(s[++i]); continue; }
                if (ch == quote) quote = '\0';
                continue;
            }
            if (ch is '"' or '\'') { quote = ch; sb.Append(ch); continue; }
            if (string.CompareOrdinal(s, i, separator, 0, separator.Length) == 0)
            {
                result.Add(sb.ToString());
                sb.Clear();
                i += separator.Length - 1;
                continue;
            }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }
}
