using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlauiCli.Core.Abstractions;

namespace FlauiCli.Drivers;

/// <summary>
/// Descendant searches that are not silently cut short by UIA3.
/// <para>
/// A UIA3 descendant search (FindAll / FindFirst with TreeScope.Descendants, or a CacheRequest with TreeScope.Subtree)
/// stops after visiting about 512 elements on some providers, without an error: on a WinForms window with 632 elements
/// FindAll(Descendants, TrueCondition) returns 516 of them, and anything further in tree order (for example a tool strip
/// after a panel of 600 buttons) is neither found nor captured. TreeWalker navigation is not affected.
/// </para>
/// <para>
/// Truncation only removes the tail of the tree order, so whatever UIA does find is valid. When a search may have been
/// cut, the missing part is recovered in two ways:
/// <list type="bullet">
/// <item>Windows: child windows with a title bar and owned windows are HWNDs, so <c>type=window</c> queries are
/// answered by enumerating them (complete, microseconds) without reading the UIA tree.</item>
/// <item>Levels: anything else is searched one level at a time (breadth first, one request per node) within
/// <see cref="SearchBudget.Remaining"/>, the time left of the command's timeout. Controls such as tool strips sit a
/// few levels down while large data (calendar cells, grid rows) is deeper, so they are reached without reading the
/// data. Reading elements is slow on some providers (about 20 ms per element on a .NET Framework 2.0 WinForms app).</item>
/// </list>
/// Both stay within UI Automation and window enumeration: nothing opens, reads or writes the target process.
/// </para>
/// <para>
/// Tables: a grid can have thousands of rows (about 20 ms per element on a .NET Framework 2.0 WinForms app), and a UIA
/// descendant search walks all of them before it reaches the controls after the grid, until it fails with
/// UIA_E_TIMEOUT. Queries that do not target table content (<see cref="DataContainers.IsTableContent"/>) therefore skip
/// the inside of tables: a UIA search that stops at the first table (UIA does not enter a table that matches), then a
/// walk that does not enter tables, and only when nothing is found outside, a search inside the tables. A query scoped
/// to a table (<c>id=grid &gt;&gt; ...</c>) searches it directly. Every UIA call is bounded by the time the command has
/// left (<see cref="UiaTimeout"/>).
/// </para>
/// </summary>
internal static class UiaSearch
{
    /// <summary>A search returning at least this many elements may have been truncated.</summary>
    public const int TruncationThreshold = 500;

    /// <summary>
    /// The descendants of <paramref name="root"/> matching <paramref name="match"/> (the client-side form of
    /// <paramref name="query"/>). Complete when the tree is small; on a truncated tree, the matches UIA found in tree
    /// order, or when it found none, those of a level-by-level search. Matches inside tables only come back when there
    /// are none outside them (see the class remarks).
    /// </summary>
    public static List<AutomationElement> FindAllDescendants(AutomationElement root, ElementQuery query, Func<AutomationElement, bool> match)
    {
        // Windows are always HWNDs (child windows, or owned windows UIA shows under their owner): under a window that has
        // one, Win32 gives the complete answer without reading the UIA tree (which can take seconds on a large form)
        if (query.Kind == ControlKind.Window && HasWindowHandle(root)) return [.. FromWindows(root, match)];
        if (!SkipsTables(root, query)) return FindAllNative(root, match);

        var result = new List<AutomationElement>();
        var tables = new List<AutomationElement>();
        Walk(root, match, result, tables, firstOnly: false);
        // Nothing outside the tables: the element may be inside one (a cell found by its name)
        for (var i = 0; i < tables.Count && result.Count == 0 && !SearchBudget.IsCancelled; i++)
        {
            try { result.AddRange(FindAllNative(tables[i], match)); }
            catch { /* the table did not answer in time, or it has gone */ }
        }
        return result;
    }

    /// <summary>
    /// The first descendant matching <paramref name="condition"/> (UIA FindFirst) or, when UIA finds none, one found by
    /// a level-by-level search with <paramref name="match"/> (the same test, client-side). Elements outside tables come
    /// first (see the class remarks).
    /// </summary>
    public static AutomationElement? FindFirstDescendant(AutomationElement root, ConditionBase condition, ElementQuery query, Func<AutomationElement, bool> match)
    {
        if (query.Kind == ControlKind.Window && HasWindowHandle(root)) return FromWindows(root, match).FirstOrDefault();

        var result = new List<AutomationElement>();
        if (!SkipsTables(root, query))
        {
            using (UiaTimeout.Limit(root.Automation, SearchBudget.Remaining))
            {
                if (root.FindFirstDescendant(condition) is { } found) return found;
            }
            Collect(root, match, result, firstOnly: true);
            return result.FirstOrDefault();
        }

        // One UIA search up to the first table in tree order: UIA does not enter a table that matches the condition, so
        // this is fast however many rows the table has, and it answers whenever the element comes before every table
        AutomationElement? first = null;
        try
        {
            using (UiaTimeout.Limit(root.Automation, SearchBudget.Remaining))
                first = root.FindFirstDescendant(new OrCondition(condition, TableCondition(root.Automation)));
        }
        catch
        {
            // Timed out or cut short: the walk below covers it
        }
        if (first is not null && match(first)) return first;

        var tables = new List<AutomationElement>();
        Walk(root, match, result, tables, firstOnly: true);
        if (result.Count > 0) return result[0];
        foreach (var table in tables)
        {
            if (SearchBudget.IsCancelled) break;
            try
            {
                using (UiaTimeout.Limit(table.Automation, SearchBudget.Remaining))
                {
                    if (table.FindFirstDescendant(condition) is { } inTable) return inTable;
                }
            }
            catch
            {
                // The table did not answer in time, or it has gone
            }
        }
        return null;
    }

    /// <summary>Whether a search under <paramref name="root"/> skips the inside of tables (the root itself is searched when it is one).</summary>
    private static bool SkipsTables(AutomationElement root, ElementQuery query) =>
        !DataContainers.IsTableContent(query.Kind)
        && !(Props.Val<ControlType>(root, root.Automation.PropertyLibrary.Element.ControlType) is { } t && DataContainers.IsTable(Mapping.ToKind(t)));

    private static OrCondition TableCondition(AutomationBase automation)
    {
        var cf = automation.ConditionFactory;
        return new OrCondition(cf.ByControlType(ControlType.DataGrid), cf.ByControlType(ControlType.Table));
    }

    /// <summary>One UIA descendant search, completed level by level when UIA may have cut it and found nothing.</summary>
    private static List<AutomationElement> FindAllNative(AutomationElement root, Func<AutomationElement, bool> match)
    {
        List<Found> all;
        using (UiaTimeout.Limit(root.Automation, SearchBudget.Remaining)) all = Search(root, TreeScope.Descendants, match);
        var result = all.Where(x => x.Matched).Select(x => x.Element).ToList();
        if (all.Count >= TruncationThreshold && result.Count == 0) Collect(root, match, result, firstOnly: false);   // breadth-first order
        return result;
    }

    /// <summary>
    /// The windows under <paramref name="root"/> matching <paramref name="match"/>: child windows with a title bar (MDI
    /// children) and owned top-level windows (dialogs), as UIA shows them.
    /// </summary>
    private static IEnumerable<AutomationElement> FromWindows(AutomationElement root, Func<AutomationElement, bool> match)
    {
        var hwnd = WindowHandle(root);
        if (hwnd == 0) yield break;

        var candidates = NativeWindows.Descendants(hwnd).Where(NativeWindows.HasCaption).Concat(NativeWindows.OwnedWindows(hwnd));
        foreach (var h in candidates)
        {
            AutomationElement? e;
            try { e = root.Automation.FromHandle(h); }
            catch { continue; }
            if (e is not null && match(e)) yield return e;
        }
    }

    private static nint WindowHandle(AutomationElement el)
    {
        try { return el.Properties.NativeWindowHandle.ValueOrDefault; }
        catch { return 0; }
    }

    private static bool HasWindowHandle(AutomationElement el) => WindowHandle(el) != 0;

    /// <summary>
    /// Adds the matching descendants of <paramref name="root"/> to <paramref name="result"/> in tree order, one request
    /// per node, without entering tables (added to <paramref name="tables"/> instead), until the first match
    /// (<paramref name="firstOnly"/>), the end of the tree or the end of <see cref="SearchBudget.Remaining"/>.
    /// </summary>
    private static void Walk(AutomationElement root, Func<AutomationElement, bool> match, List<AutomationElement> result,
        List<AutomationElement> tables, bool firstOnly)
    {
        var budget = SearchBudget.Remaining;
        var elapsed = Stopwatch.StartNew();
        Visit(root);

        // True when the walk is over
        bool Visit(AutomationElement el)
        {
            var left = budget - elapsed.Elapsed;
            if (left <= TimeSpan.Zero || SearchBudget.IsCancelled) return true;
            List<Found> children;
            try
            {
                using (UiaTimeout.Limit(el.Automation, left)) children = Search(el, TreeScope.Children, match);
            }
            catch
            {
                return false;   // the element may have gone, or it did not answer in time
            }
            foreach (var child in children)
            {
                if (child.Matched)
                {
                    result.Add(child.Element);
                    if (firstOnly) return true;
                }
                if (child.IsTable) tables.Add(child.Element);
                else if (Visit(child.Element)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Adds the matching descendants of <paramref name="root"/> to <paramref name="result"/>, searching one level at a
    /// time (breadth first, one request per node) until the first match (<paramref name="firstOnly"/>), the end of the
    /// tree or the end of <see cref="SearchBudget.Remaining"/>.
    /// </summary>
    private static void Collect(AutomationElement root, Func<AutomationElement, bool> match, List<AutomationElement> result, bool firstOnly)
    {
        var budget = SearchBudget.Remaining;
        var elapsed = Stopwatch.StartNew();
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        while (queue.Count > 0 && elapsed.Elapsed <= budget && !SearchBudget.IsCancelled)
        {
            List<Found> children;
            try
            {
                using (UiaTimeout.Limit(root.Automation, budget - elapsed.Elapsed)) children = Search(queue.Dequeue(), TreeScope.Children, match);
            }
            catch { continue; }   // the element may have gone
            foreach (var (child, matched, _) in children)
            {
                if (matched)
                {
                    result.Add(child);
                    if (firstOnly) return;
                }
                queue.Enqueue(child);
            }
        }
    }

    private readonly record struct Found(AutomationElement Element, bool Matched, bool IsTable);

    /// <summary>
    /// FindAll with TrueCondition, testing each element while the properties used by selectors are cached (the test
    /// must run inside the CacheRequest scope, otherwise every property read is a cross-process call).
    /// </summary>
    private static List<Found> Search(AutomationElement el, TreeScope scope, Func<AutomationElement, bool> match)
    {
        var lib = el.Automation.PropertyLibrary;
        // Full: the elements are returned to callers and searched again (FlaUI's default None leaves them unusable)
        var cr = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        cr.Add(lib.Element.Name);
        cr.Add(lib.Element.AutomationId);
        cr.Add(lib.Element.ClassName);
        cr.Add(lib.Element.ControlType);
        cr.Add(lib.Element.ProcessId);
        using (cr.Activate())
        {
            return [.. el.FindAll(scope, TrueCondition.Default).Select(e => new Found(e, match(e), IsTable(e)))];
        }
    }

    private static bool IsTable(AutomationElement e) =>
        Props.Val<ControlType>(e, e.Automation.PropertyLibrary.Element.ControlType) is { } t && DataContainers.IsTable(Mapping.ToKind(t));
}
