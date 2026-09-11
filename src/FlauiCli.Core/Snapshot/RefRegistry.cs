using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Snapshot;

/// <summary>Element behind a ref plus the hints needed to find it again.</summary>
public sealed record RefEntry(string Ref, IUiElement Element, string AutomationId, string Name, ControlKind Kind);

/// <summary>
/// Manages snapshot refs (e1, e2...). The same element (identified by RuntimeId) keeps the same ref across
/// snapshots, so agents can keep using refs they obtained earlier.
/// </summary>
public sealed class RefRegistry
{
    private readonly Dictionary<string, RefEntry> _byRef = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byRuntimeId = new(StringComparer.Ordinal);
    private int _next = 1;

    public int Count => _byRef.Count;

    public string Assign(IUiElement el, string? runtimeId, string automationId, string name, ControlKind kind)
    {
        if (!string.IsNullOrEmpty(runtimeId) && _byRuntimeId.TryGetValue(runtimeId, out var existing))
        {
            _byRef[existing] = new RefEntry(existing, el, automationId, name, kind);
            return existing;
        }

        var r = "e" + _next++;
        _byRef[r] = new RefEntry(r, el, automationId, name, kind);
        if (!string.IsNullOrEmpty(runtimeId)) _byRuntimeId[runtimeId] = r;
        return r;
    }

    public string Assign(ElementNode n) => Assign(n.Element, n.RuntimeId, n.AutomationId, n.Name, n.Kind);

    /// <summary>Gets (or assigns) the ref of any element.</summary>
    public string GetOrAssign(IUiElement el) => Assign(el, el.RuntimeId, el.AutomationId, el.Name, el.Kind);

    public bool TryGet(string r, out RefEntry entry) => _byRef.TryGetValue(r, out entry!);

    public void Update(string r, IUiElement el)
    {
        if (_byRef.TryGetValue(r, out var e)) _byRef[r] = e with { Element = el };
    }

    public void Clear()
    {
        _byRef.Clear();
        _byRuntimeId.Clear();
        _next = 1;
    }
}
