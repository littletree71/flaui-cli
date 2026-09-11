using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Targeting;

/// <summary>
/// Generates a stable, reusable selector for an element (used by recording and scripts).
/// Priority: AutomationId → AutomationId+type → type+name → add nth → XPath.
/// </summary>
internal static class SelectorGenerator
{
    public static string Generate(IUiElement el, IReadOnlyList<IUiElement> roots)
    {
        var aid = el.AutomationId;
        var name = el.Name;
        var kind = el.Kind.ToString();

        var candidates = new List<string>();
        if (aid.Length > 0 && !LooksGenerated(aid))
        {
            candidates.Add($"id={Selector.Quote(aid)}");
            candidates.Add($"id={Selector.Quote(aid)}&&type={kind}");
        }
        if (name.Length > 0) candidates.Add($"type={kind}&&name={Selector.Quote(name)}");

        (string Selector, int Index)? fallback = null;
        foreach (var c in candidates)
        {
            List<IUiElement> matches;
            try { matches = ElementFinder.FindAll(Selector.Parse(c), roots); }
            catch (CliException) { continue; }

            var idx = matches.FindIndex(m => m.Equals(el));
            if (idx < 0) continue;
            if (matches.Count == 1) return c;
            fallback ??= (c, idx);
        }

        if (fallback is { } f) return $"{f.Selector}&&nth={f.Index}";

        if (roots.Count > 0 && el.GetXPathFrom(roots[0]) is { Length: > 0 } xpath) return "xpath=" + xpath;

        return candidates.FirstOrDefault() ?? $"type={kind}";
    }

    /// <summary>Whether an AutomationId looks generated at runtime and therefore unstable (for example a numeric HWND or a GUID).</summary>
    internal static bool LooksGenerated(string aid) =>
        Guid.TryParse(aid, out _) || (aid.Length > 6 && aid.All(char.IsDigit));
}
