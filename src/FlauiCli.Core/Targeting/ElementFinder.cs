using System.Text.RegularExpressions;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Targeting;

/// <summary>Finds elements under the given roots using a <see cref="Selector"/> (refs are resolved elsewhere).</summary>
internal static class ElementFinder
{
    private static readonly Dictionary<string, ControlKind> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["textbox"] = ControlKind.Edit,
        ["input"] = ControlKind.Edit,
        ["label"] = ControlKind.Text,
        ["dropdown"] = ControlKind.ComboBox,
        ["link"] = ControlKind.Hyperlink,
        ["radio"] = ControlKind.RadioButton,
        ["tabpage"] = ControlKind.TabItem,
        ["grid"] = ControlKind.DataGrid,
    };

    public static ControlKind ParseKind(string value)
    {
        if (Enum.TryParse<ControlKind>(value, ignoreCase: true, out var k) && Enum.IsDefined(k) && !int.TryParse(value, out _)) return k;
        if (Aliases.TryGetValue(value, out k)) return k;
        throw new CliException($"Unknown type: {value}. Valid values: {string.Join(", ", Enum.GetNames<ControlKind>())}");
    }

    public static List<IUiElement> FindAll(Selector selector, IReadOnlyList<IUiElement> roots)
    {
        if (selector.IsRef) throw new InvalidOperationException("Refs must be resolved by TargetResolver");

        IReadOnlyList<IUiElement> current = roots;
        foreach (var part in selector.Parts)
        {
            current = FindPartUnder(current, part);
            if (current.Count == 0) break;
        }

        return [.. current];
    }

    /// <summary>
    /// The element <see cref="FindAll"/> would return first, without collecting every match of the last part when
    /// that can be avoided: a UIA FindFirst stops at the first match instead of walking the whole tree, which
    /// matters for windows with thousands of elements.
    /// </summary>
    public static IUiElement? FindFirst(Selector selector, IReadOnlyList<IUiElement> roots)
    {
        if (selector.IsRef) throw new InvalidOperationException("Refs must be resolved by TargetResolver");

        IReadOnlyList<IUiElement> current = roots;
        var parts = selector.Parts;
        for (var i = 0; i < parts.Count - 1; i++)
        {
            current = FindPartUnder(current, parts[i]);
            if (current.Count == 0) return null;
        }

        var last = parts[^1];
        if (ToQuery(last) is { } query)
        {
            // FindAll puts visible matches first and keeps tree order otherwise. The first match under the first
            // root that has any is therefore the answer when it is visible; when it is off-screen a visible match
            // may follow, so fall back to the full search.
            var anyMatch = false;
            foreach (var root in current)
            {
                var found = root.FindFirst(query);
                if (found is null) continue;
                // Rows that were asked for are usually off-screen: looking for a visible match would read every row after them
                if (SearchBudget.Rows != default || !found.IsOffscreen) return found;
                anyMatch = true;
                break;
            }
            if (!anyMatch) return null;
        }

        return FindPartUnder(current, last).FirstOrDefault();
    }

    /// <summary>All matches of one selector part under each root: duplicates removed, visible elements first, nth applied.</summary>
    private static List<IUiElement> FindPartUnder(IReadOnlyList<IUiElement> roots, SelectorPart part)
    {
        var next = new List<IUiElement>();
        foreach (var root in roots)
        {
            foreach (var found in FindPart(root, part))
            {
                // Comparing is a cross-process call, and a search inside a table can return thousands of elements
                if (roots.Count == 1 || !next.Any(e => e.Equals(found))) next.Add(found);
            }
        }

        next = VisibleFirst(next);
        if (part.Nth is int nth) next = nth < next.Count ? [next[nth]] : [];
        return next;
    }

    /// <summary>
    /// Visible elements first, otherwise in tree order. Reading the state is a cross-process call per element, so this
    /// stays in tree order when specific rows were asked for or the search did not finish, and stops at the deadline.
    /// </summary>
    private static List<IUiElement> VisibleFirst(List<IUiElement> elements)
    {
        if (SearchBudget.Rows != default || SearchBudget.Incomplete is not null) return elements;

        var visible = new List<IUiElement>();
        var offscreen = new List<IUiElement>();
        for (var i = 0; i < elements.Count; i++)
        {
            if (SearchBudget.IsExpired)
            {
                offscreen.AddRange(elements.Skip(i));
                break;
            }
            (elements[i].IsOffscreen ? offscreen : visible).Add(elements[i]);
        }
        return [.. visible, .. offscreen];
    }

    /// <summary>The UIA query of a part, or null when the part needs client-side filtering (xpath, text=, regex=, nth).</summary>
    private static ElementQuery? ToQuery(SelectorPart part)
    {
        if (part.XPath is not null || part.Nth is not null) return null;
        string? id = null, name = null, cls = null;
        ControlKind? kind = null;
        foreach (var c in part.Conditions)
        {
            switch (c.Key)
            {
                case "id": id = c.Value; break;
                case "name": name = c.Value; break;
                case "class": cls = c.Value; break;
                case "type": kind = ParseKind(c.Value); break;
                default: return null;
            }
        }
        return new ElementQuery(id, name, cls, kind);
    }

    private static IEnumerable<IUiElement> FindPart(IUiElement root, SelectorPart part)
    {
        if (part.XPath is not null) return root.FindByXPath(part.XPath);

        string? id = null, name = null, cls = null, contains = null;
        Regex? pattern = null;
        ControlKind? kind = null;
        foreach (var c in part.Conditions)
        {
            switch (c.Key)
            {
                case "id": id = c.Value; break;
                case "name": name = c.Value; break;
                case "class": cls = c.Value; break;
                case "type": kind = ParseKind(c.Value); break;
                case "text": contains = c.Value; break;
                case "regex": pattern = new Regex(c.Value, RegexOptions.IgnoreCase); break;
            }
        }

        IEnumerable<IUiElement> found = root.FindAll(new ElementQuery(id, name, cls, kind));
        if (contains is not null) found = found.Where(e => e.Name.Contains(contains, StringComparison.OrdinalIgnoreCase));
        if (pattern is not null) found = found.Where(e => pattern.IsMatch(e.Name));
        return found;
    }
}
