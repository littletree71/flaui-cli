using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlauiCli.Core;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Drivers;

/// <summary>
/// Fetches a subtree (Control View) with one UIA CacheRequest per node: each request returns the node together with
/// the properties of its children, which keeps cross-process calls low.
/// <para>
/// The tree is read one node at a time rather than with one Subtree request: UIA3 silently cuts a Subtree request
/// after about 512 elements (see <see cref="UiaSearch"/>), and on a window with a large table such a request walks every
/// row (about 20 ms per element on a .NET Framework 2.0 WinForms app) until it fails with UIA_E_TIMEOUT, so nothing
/// after the table is captured. Tables and lists (<see cref="DataContainers.IsCapped"/>) are read item by item up to the
/// row limit, and the rest is counted (<see cref="ElementNode.OmittedChildren"/>) instead of read.
/// </para>
/// </summary>
internal static class TreeCapture
{
    /// <summary>Node limit so that huge trees do not blow up the output.</summary>
    public const int MaxNodes = 5000;

    /// <summary>How long a snapshot may read the tree (longer when the command's --timeout allows more).</summary>
    private static readonly TimeSpan FullBudget = TimeSpan.FromSeconds(20);

    /// <summary>The same for a capture that does not need to be complete (the snapshot saved after every action).</summary>
    private static readonly TimeSpan QuickBudget = TimeSpan.FromSeconds(5);

    /// <param name="complete">Whether the capture may take <see cref="FullBudget"/>; false stops after <see cref="QuickBudget"/>.</param>
    /// <param name="maxRows">Items read from each table or list; 0 reads all of them.</param>
    public static ElementNode Capture(AutomationElement root, CaptureDepth? depth = null, bool complete = true, int maxRows = 0)
    {
        var budget = complete ? FullBudget : QuickBudget;
        if (complete && SearchBudget.Remaining > budget) budget = SearchBudget.Remaining;
        var walker = new Walker(depth is { MaxDepth: > 0 } ? depth : null, maxRows, budget);
        // Some providers do not support caching; fall back to reading element by element
        var tree = walker.Capture(root) ?? walker.ElementByElement(root);
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
        cr.Add(lib.Grid.RowCount);
        return cr;
    }

    /// <summary>One capture: the node count, the time budget and the limits.</summary>
    private sealed class Walker(CaptureDepth? depth, int maxRows, TimeSpan budget)
    {
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private int _count;

        private TimeSpan Left => budget - _elapsed.Elapsed;

        private bool Stopped => _count >= MaxNodes || Left <= TimeSpan.Zero || SearchBudget.IsCancelled;

        private bool Caps(ElementNode node) => maxRows > 0 && DataContainers.IsCapped(node.Kind);

        /// <summary>The capture of <paramref name="root"/>, or null when the provider does not support caching.</summary>
        public ElementNode? Capture(AutomationElement root)
        {
            try
            {
                using (NewCacheRequest(root.Automation, TreeScope.Element | TreeScope.Children).Activate())
                {
                    // The root alone first: when it is a table, its rows are read one by one instead of all at once
                    AutomationElement? self;
                    using (UiaTimeout.Limit(root.Automation, Left))
                    using (NewCacheRequest(root.Automation, TreeScope.Element).Activate())
                        self = root.FindFirst(TreeScope.Element, TrueCondition.Default);
                    if (self is null) return null;
                    _count = 1;
                    var node = CreateNode(self);
                    if (Caps(node))
                    {
                        AddChildren(node, self, ReadItems(node, self, expand: true), level: 0, transparent: false);
                        return node;
                    }

                    AutomationElement? withChildren;
                    using (UiaTimeout.Limit(root.Automation, Left)) withChildren = root.FindFirst(TreeScope.Element, TrueCondition.Default);
                    if (withChildren is not null) AddChildren(node, withChildren, level: 0, transparent: false);
                    return node;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Adds the children of <paramref name="el"/> to <paramref name="node"/>; their own children are fetched while
        /// the depth allows. <paramref name="level"/> is the printed depth of the node; a transparent node is not
        /// printed, so its children stay on the same level. Runs inside a CacheRequest with Element and Children scope.
        /// </summary>
        private void AddChildren(ElementNode node, AutomationElement el, int level, bool transparent) =>
            AddChildren(node, el, Children(node, el, depth?.ExpandsChildren(level, transparent) ?? true), level, transparent);

        private void AddChildren(ElementNode node, AutomationElement el, Items children, int level, bool transparent)
        {
            var expand = depth?.ExpandsChildren(level, transparent) ?? true;
            var childLevel = CaptureDepth.ChildLevel(level, transparent);
            for (var i = 0; i < children.Elements.Count; i++)
            {
                if (Stopped)
                {
                    node.OmittedChildren = ElementNode.UnknownCount;
                    break;
                }
                var child = children.Elements[i];
                _count++;
                var childNode = CreateNode(child);
                node.Children.Add(childNode);
                if (!expand) continue;

                var childTransparent = depth?.IsTransparent(childNode) ?? false;
                if (Caps(childNode))
                {
                    // Never request all children of a table at once: read its first rows one by one
                    AddChildren(childNode, child, ReadItems(childNode, child, depth?.ExpandsChildren(childLevel, childTransparent) ?? true), childLevel, childTransparent);
                    continue;
                }
                if (children.WithChildren)
                {
                    AddChildren(childNode, child, childLevel, childTransparent);
                    continue;
                }

                AutomationElement? withChildren;
                try
                {
                    using (UiaTimeout.Limit(child.Automation, Left)) withChildren = child.FindFirst(TreeScope.Element, TrueCondition.Default);
                }
                catch (Exception ex) when (UiaErrors.IsTimeout(ex) && !Stopped)
                {
                    // The children did not arrive in time all at once (one of them may be a large table): read them one by one
                    AddChildren(childNode, child, ReadItems(childNode, child, expand: true, limit: int.MaxValue), childLevel, childTransparent);
                    continue;
                }
                catch
                {
                    withChildren = null;   // the element went away
                }
                if (withChildren is not null) AddChildren(childNode, withChildren, childLevel, childTransparent);
            }
        }

        /// <summary>The cached children of <paramref name="el"/>; a capped container's first items instead.</summary>
        private Items Children(ElementNode node, AutomationElement el, bool expand)
        {
            if (Caps(node)) return ReadItems(node, el, expand);
            try { return new Items(el.CachedChildren, WithChildren: false); }
            catch { return new Items([], WithChildren: false); }
        }

        /// <summary>
        /// The first children of <paramref name="el"/>, read one by one with a TreeWalker (each with its own children
        /// cached), up to the row limit, or only the first one when <paramref name="expand"/> is false (the snapshot then
        /// only shows that there are children). Children that are not read are counted in <see cref="ElementNode.OmittedChildren"/>:
        /// from the table's row count when it has one, else as an unknown number.
        /// </summary>
        private Items ReadItems(ElementNode node, AutomationElement el, bool expand, int? limit = null)
        {
            var max = limit ?? (expand ? maxRows : 1);
            var result = new List<AutomationElement>();
            var walker = el.Automation.TreeWalkerFactory.GetControlViewWalker();
            try
            {
                using (UiaTimeout.Limit(el.Automation, Left))
                {
                    var child = walker.GetFirstChild(el);
                    while (child is not null && result.Count < max && !Stopped)
                    {
                        result.Add(child);
                        child = walker.GetNextSibling(child);
                    }
                    if (child is not null && expand) node.OmittedChildren = Omitted(el, result);
                }
            }
            catch
            {
                // The element went away, or it did not answer in time: keep what was read
                if (expand) node.OmittedChildren = ElementNode.UnknownCount;
            }
            return new Items(result, WithChildren: true);
        }

        /// <summary>
        /// The number of rows that were not read, from the GridPattern row count (rows only: header rows and scroll bars
        /// read among the children are not counted), or <see cref="ElementNode.UnknownCount"/>.
        /// </summary>
        private static int Omitted(AutomationElement table, List<AutomationElement> read)
        {
            var rows = Props.Val<int>(table, table.Automation.PropertyLibrary.Grid.RowCount);
            var readRows = read.Count(e => Props.Val<ControlType>(e, e.Automation.PropertyLibrary.Element.ControlType) is not
                (ControlType.Header or ControlType.HeaderItem or ControlType.ScrollBar));
            return rows is int total && total > readRows ? total - readRows : ElementNode.UnknownCount;
        }

        /// <summary>
        /// Reads the tree without caching (for providers that do not support it), within the same limits; the items of a
        /// table past the row limit are not read.
        /// </summary>
        public ElementNode ElementByElement(AutomationElement el)
        {
            _count++;
            var node = CreateNode(el);
            if (Stopped) return node;

            try
            {
                using (UiaTimeout.Limit(el.Automation, Left))
                {
                    var walker = el.Automation.TreeWalkerFactory.GetControlViewWalker();
                    var max = Caps(node) ? maxRows : int.MaxValue;
                    var child = walker.GetFirstChild(el);
                    for (var read = 0; child is not null; read++)
                    {
                        if (read >= max || Stopped)
                        {
                            node.OmittedChildren = ElementNode.UnknownCount;
                            break;
                        }
                        node.Children.Add(ElementByElement(child));
                        child = walker.GetNextSibling(child);
                    }
                }
            }
            catch
            {
                // The element went away, or it did not answer in time
            }

            return node;
        }
    }

    /// <param name="WithChildren">Whether each element was fetched together with its own children.</param>
    private sealed record Items(IReadOnlyList<AutomationElement> Elements, bool WithChildren);

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
