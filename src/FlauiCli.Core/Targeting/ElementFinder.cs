using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Targeting;

/// <summary>依 <see cref="Selector"/> 在指定根元素之下尋找元素（不含 ref 解析）。</summary>
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
        throw new CliException($"未知的 type：{value}。可用值：{string.Join(", ", Enum.GetNames<ControlKind>())}");
    }

    public static List<IUiElement> FindAll(Selector selector, IReadOnlyList<IUiElement> roots)
    {
        if (selector.IsRef) throw new InvalidOperationException("ref 必須由 TargetResolver 解析");

        IReadOnlyList<IUiElement> current = roots;
        foreach (var part in selector.Parts)
        {
            var next = new List<IUiElement>();
            foreach (var root in current)
            {
                foreach (var found in FindPart(root, part))
                {
                    if (!next.Any(e => e.Equals(found))) next.Add(found);
                }
            }

            // 可見元素優先（穩定排序，保留原本的樹狀順序）
            next = [.. next.OrderBy(e => e.IsOffscreen)];
            if (part.Nth is int nth) next = nth < next.Count ? [next[nth]] : [];
            current = next;
            if (current.Count == 0) break;
        }

        return [.. current];
    }

    private static IEnumerable<IUiElement> FindPart(IUiElement root, SelectorPart part)
    {
        if (part.XPath is not null) return root.FindByXPath(part.XPath);

        string? id = null, name = null, cls = null, contains = null;
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
            }
        }

        var found = root.FindAll(new ElementQuery(id, name, cls, kind));
        return contains is null
            ? found
            : found.Where(e => e.Name.Contains(contains, StringComparison.OrdinalIgnoreCase));
    }
}
