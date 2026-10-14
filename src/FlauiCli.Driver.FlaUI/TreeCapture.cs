using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Drivers;

/// <summary>Fetches a whole subtree (Control View) with one UIA CacheRequest, drastically reducing cross-process calls.</summary>
internal static class TreeCapture
{
    /// <summary>Node limit so that huge data grids do not blow up the output.</summary>
    public const int MaxNodes = 5000;

    /// <summary>How long filling in a capture UIA cut short may take (a snapshot is not bound to the command timeout).</summary>
    private static readonly TimeSpan RepairBudget = TimeSpan.FromSeconds(10);

    /// <param name="complete">Fill in a capture UIA cut short (see <see cref="Repair"/>); false keeps it as is.</param>
    public static ElementNode Capture(AutomationElement root, CaptureDepth? depth = null, bool complete = true)
    {
        var tree = (depth is { MaxDepth: > 0 } ? CaptureLimited(root, depth) : null) ?? CaptureRaw(root, complete);
        // SelectionPattern is not cached: fill in the selected value of non-editable ComboBoxes (usually only a few)
        foreach (var n in tree.DescendantsAndSelf().Where(n => n.Kind == ControlKind.ComboBox && n.Value is null))
            n.Value = FlaUIElement.SelectedItemName(((FlaUIElement)n.Element).Inner);
        return tree;
    }

    private static CacheRequest NewCacheRequest(AutomationBase automation, TreeScope scope)
    {
        var lib = automation.PropertyLibrary;
        var cr = new CacheRequest
        {
            TreeScope = scope,
            TreeFilter = lib.Element.IsControlElement.GetCondition(true),
            // FlaUI defaults to None: cached elements then have no live reference, and every later call on them
            // (FindFirst for the next level, actions on snapshot refs) fails with E_FAIL
            AutomationElementMode = AutomationElementMode.Full,
        };
        cr.Add(lib.Element.Name);
        cr.Add(lib.Element.AutomationId);
        cr.Add(lib.Element.ControlType);
        cr.Add(lib.Element.ClassName);
        cr.Add(lib.Element.IsEnabled);
        cr.Add(lib.Element.IsOffscreen);
        cr.Add(lib.Element.BoundingRectangle);
        cr.Add(lib.Element.RuntimeId);
        cr.Add(lib.Element.HasKeyboardFocus);
        cr.Add(lib.Toggle.ToggleState);
        cr.Add(lib.Value.Value);
        cr.Add(lib.ExpandCollapse.ExpandCollapseState);
        cr.Add(lib.SelectionItem.IsSelected);
        return cr;
    }

    private static ElementNode CaptureRaw(AutomationElement root, bool complete)
    {
        var count = 0;
        // Some providers do not support caching; fall back to reading element by element
        return CaptureSubtree(root, ref count, repairTime: null, complete) ?? Build(root, cachedMode: false, ref count);
    }

    /// <summary>
    /// Captures the subtree of <paramref name="root"/> with one Subtree CacheRequest. UIA3 silently cuts such a request
    /// after about 512 elements (see <see cref="UiaSearch"/>); when the capture is that large, the missing part is filled
    /// in by <see cref="Repair"/> for at most <see cref="RepairBudget"/>. Null when caching is not supported.
    /// </summary>
    private static ElementNode? CaptureSubtree(AutomationElement root, ref int count, Stopwatch? repairTime, bool complete = true)
    {
        var start = count;
        ElementNode whole;
        try
        {
            using (NewCacheRequest(root.Automation, TreeScope.Subtree).Activate())
            {
                var cached = root.FindFirst(TreeScope.Element, TrueCondition.Default);
                if (cached is null) return null;
                whole = Build(cached, cachedMode: true, ref count);
            }
        }
        catch
        {
            return null;
        }

        if (!complete || count - start < UiaSearch.TruncationThreshold || count >= MaxNodes) return whole;
        repairTime ??= Stopwatch.StartNew();
        Repair(root, whole, ref count, repairTime);
        return whole;
    }

    /// <summary>
    /// Completes <paramref name="cut"/>, a capture of <paramref name="el"/> that UIA may have cut. The cut keeps a prefix
    /// of the tree in pre-order, so among the children of a node, those before the last child that has children of its
    /// own are complete, that child may be cut further down (repaired recursively), and the children after it were not
    /// expanded (captured now). Only the missing part is read again. Stops filling in after the time budget.
    /// </summary>
    private static void Repair(AutomationElement el, ElementNode cut, ref int count, Stopwatch elapsed)
    {
        if (elapsed.Elapsed > RepairBudget || count >= MaxNodes) return;

        // The real children of el, with their properties (one request)
        List<(AutomationElement Element, ElementNode Node)> children;
        try
        {
            using (NewCacheRequest(el.Automation, TreeScope.Element | TreeScope.Children).Activate())
            {
                var cached = el.FindFirst(TreeScope.Element, TrueCondition.Default);
                if (cached is null) return;
                children = [.. cached.CachedChildren.Select(c => (c, CreateNode(c)))];
            }
        }
        catch
        {
            return;
        }

        var lastExpanded = cut.Children.FindLastIndex(c => c.Children.Count > 0);
        var position = new Dictionary<string, int>();
        for (var i = 0; i < cut.Children.Count; i++)
        {
            if (cut.Children[i].RuntimeId is { } id) position.TryAdd(id, i);
        }

        var repaired = new List<ElementNode>();
        foreach (var (child, fresh) in children)
        {
            var i = fresh.RuntimeId is { } id && position.TryGetValue(id, out var p) ? p : -1;
            if (i >= 0 && i < lastExpanded)
            {
                repaired.Add(cut.Children[i]);   // entirely before the cut: complete
            }
            else if (i >= 0 && i == lastExpanded)
            {
                Repair(child, cut.Children[i], ref count, elapsed);   // the cut runs through this subtree
                repaired.Add(cut.Children[i]);
            }
            else if (elapsed.Elapsed <= RepairBudget && count < MaxNodes)
            {
                repaired.Add(CaptureSubtree(child, ref count, elapsed) ?? fresh);   // after the cut: not read yet
            }
            else
            {
                repaired.Add(i >= 0 ? cut.Children[i] : fresh);
            }
        }

        cut.Children.Clear();
        cut.Children.AddRange(repaired);
    }

    /// <summary>
    /// Depth-limited capture: fetches one level at a time (each node together with its children in one CacheRequest)
    /// and stops below the last printed level, so a shallow snapshot of a huge window does not walk the whole tree.
    /// Returns null when the provider does not support caching (the caller then captures the whole tree).
    /// </summary>
    private static ElementNode? CaptureLimited(AutomationElement root, CaptureDepth depth)
    {
        try
        {
            using (NewCacheRequest(root.Automation, TreeScope.Element | TreeScope.Children).Activate())
            {
                var cached = root.FindFirst(TreeScope.Element, TrueCondition.Default);
                if (cached is null) return null;
                var count = 1;
                var node = CreateNode(cached);
                AddLimitedChildren(node, cached, level: 0, transparent: false, depth, ref count);
                return node;
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Adds the children of <paramref name="el"/> (whose children are cached). <paramref name="level"/> is the
    /// printed depth of the node; a transparent node is not printed, so its children stay on the same level.
    /// </summary>
    private static void AddLimitedChildren(ElementNode node, AutomationElement el, int level, bool transparent, CaptureDepth depth, ref int count)
    {
        AutomationElement[] children;
        try { children = el.CachedChildren; }
        catch { children = []; }

        var expand = depth.ExpandsChildren(level, transparent);
        var childLevel = CaptureDepth.ChildLevel(level, transparent);
        foreach (var child in children)
        {
            if (count >= MaxNodes) break;
            count++;
            var childNode = CreateNode(child);
            node.Children.Add(childNode);
            if (!expand) continue;

            AutomationElement? withChildren;
            try { withChildren = child.FindFirst(TreeScope.Element, TrueCondition.Default); }
            catch { withChildren = null; }
            if (withChildren is not null)
                AddLimitedChildren(childNode, withChildren, childLevel, depth.IsTransparent(childNode), depth, ref count);
        }
    }

    private static ElementNode Build(AutomationElement el, bool cachedMode, ref int count)
    {
        count++;
        var node = CreateNode(el);
        if (count >= MaxNodes) return node;

        AutomationElement[] children;
        try { children = cachedMode ? el.CachedChildren : el.FindAllChildren(); }
        catch { children = []; }

        foreach (var child in children)
        {
            if (count >= MaxNodes) break;
            node.Children.Add(Build(child, cachedMode, ref count));
        }

        return node;
    }

    /// <summary>Reads the properties of one element (from the cache while a CacheRequest is active).</summary>
    private static ElementNode CreateNode(AutomationElement el)
    {
        var lib = el.Automation.PropertyLibrary;
        return new ElementNode
        {
            Element = new FlaUIElement(el),
            Name = Props.Ref<string>(el, lib.Element.Name) ?? "",
            AutomationId = Props.Ref<string>(el, lib.Element.AutomationId) ?? "",
            Kind = Props.Val<ControlType>(el, lib.Element.ControlType) is { } ct ? Mapping.ToKind(ct) : ControlKind.Unknown,
            ClassName = Props.Ref<string>(el, lib.Element.ClassName) ?? "",
            IsEnabled = Props.Val<bool>(el, lib.Element.IsEnabled) ?? true,
            IsOffscreen = Props.Val<bool>(el, lib.Element.IsOffscreen) ?? false,
            Bounds = Props.Bounds(el),
            RuntimeId = Props.RuntimeId(el),
            Toggle = Props.Val<ToggleState>(el, lib.Toggle.ToggleState) is { } ts ? Mapping.ToToggle(ts) : null,
            Value = Props.Ref<string>(el, lib.Value.Value),
            Expand = Props.Val<ExpandCollapseState>(el, lib.ExpandCollapse.ExpandCollapseState) is { } es ? Mapping.ToExpand(es) : null,
            IsSelected = Props.Val<bool>(el, lib.SelectionItem.IsSelected),
            HasFocus = Props.Val<bool>(el, lib.Element.HasKeyboardFocus) ?? false,
        };
    }
}
