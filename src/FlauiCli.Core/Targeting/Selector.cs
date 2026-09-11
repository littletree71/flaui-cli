using System.Text;
using System.Text.RegularExpressions;

namespace FlauiCli.Core.Targeting;

/// <summary>單一條件，例如 id=num1Button。</summary>
public sealed record SelectorCondition(string Key, string Value);

/// <summary>以 &amp;&amp; 串接的一組條件；多個 part 以 &gt;&gt; 表示「在前者之內」。</summary>
public sealed class SelectorPart
{
    public List<SelectorCondition> Conditions { get; } = [];

    /// <summary>第幾個符合者（從 0 起算）。</summary>
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
/// 元素定位字串。支援：
/// <list type="bullet">
/// <item>ref：<c>e12</c>（來自 snapshot）</item>
/// <item><c>id=</c>AutomationId、<c>name=</c>名稱完全相符、<c>text=</c>名稱包含、<c>type=</c>ControlType、<c>class=</c>ClassName、<c>nth=</c>索引</item>
/// <item><c>xpath=</c>FlaUI XPath</item>
/// <item>以 <c>&amp;&amp;</c> 組合條件、以 <c>&gt;&gt;</c> 表示階層</item>
/// <item>沒有前綴的字串視為 name</item>
/// </list>
/// </summary>
public sealed partial class Selector
{
    public static readonly string[] KnownKeys = ["id", "name", "text", "type", "class", "nth", "xpath"];

    private Selector(string raw) => Raw = raw;

    public string Raw { get; }

    /// <summary>若為 snapshot ref（e12）則有值。</summary>
    public string? Ref { get; private init; }

    public List<SelectorPart> Parts { get; } = [];

    public bool IsRef => Ref is not null;

    [GeneratedRegex(@"^e\d+$")]
    private static partial Regex RefPattern();

    public static bool LooksLikeRef(string s) => RefPattern().IsMatch(s.Trim());

    public static Selector Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new CliException("目標選擇器不可為空");
        var text = raw.Trim();
        if (LooksLikeRef(text)) return new Selector(text) { Ref = text };

        var selector = new Selector(text);
        foreach (var partText in SplitOutsideQuotes(text, ">>"))
        {
            var part = new SelectorPart();
            var trimmed = partText.Trim();
            if (trimmed.StartsWith("xpath=", StringComparison.OrdinalIgnoreCase))
            {
                // xpath 內可能含 &&，因此整段視為 xpath
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
                                : throw new CliException($"nth 必須是非負整數：{value}");
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
                    // 沒有已知前綴：視為名稱完全相符
                    part.Conditions.Add(new SelectorCondition("name", Unquote(c)));
                }
            }

            if (part.Conditions.Count == 0 && part.XPath is null)
                throw new CliException($"無效的選擇器：{raw}");
            selector.Parts.Add(part);
        }

        return selector;
    }

    /// <summary>必要時為值加上雙引號（含空白、&amp;&amp;、&gt;&gt; 或引號時）。</summary>
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
