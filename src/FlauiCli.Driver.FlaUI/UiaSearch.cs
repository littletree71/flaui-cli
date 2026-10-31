using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlauiCli.Core;
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
/// <item>Nodes: anything else is searched one node at a time within <see cref="SearchBudget.Remaining"/>, the time left
/// of the command's timeout. Reading elements is slow on some providers (about 20 ms per element on a .NET Framework
/// 2.0 WinForms app).</item>
/// </list>
/// Both stay within UI Automation and window enumeration: nothing opens, reads or writes the target process.
/// </para>
/// <para>
/// Tables: a grid can have thousands of rows (about 20 ms per element on a .NET Framework 2.0 WinForms app), and a UIA
/// descendant search walks all of them before it reaches the controls after the grid, until it fails with
/// UIA_E_TIMEOUT. Queries that do not target table content (<see cref="DataContainers.IsTableContent"/>) therefore skip
/// the inside of tables: a UIA search that stops at the first table (UIA does not enter a table that matches), then a
/// walk that does not enter tables. They never fall back to a search inside the tables.
/// </para>
/// <para>
/// A query that does target table content, or is scoped to a table (<c>id=grid &gt;&gt; ...</c>), reads the tree one node
/// per request (<see cref="ReadNodes"/>), never with one UIA descendant search: a timeout only ends the client's wait, so
/// the application keeps serving such a search for minutes, and every later request waits behind it. Reading node by node
/// can stop at the deadline, and the application is free again right away. Rows that were not reached by then are
/// reported through <see cref="SearchBudget.Incomplete"/>.
/// </para>
/// </summary>
internal static class UiaSearch
{
    /// <summary>Longest wait for one node, so an application that stopped answering ends the search soon after the deadline.</summary>
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(2);

    private const string NoAnswer = "the application did not answer in time, so the search is incomplete";

    /// <summary>
    /// The descendants of <paramref name="root"/> matching <paramref name="match"/> (the client-side form of
    /// <paramref name="query"/>), in tree order, as far as the time left allows. The inside of tables is left out unless
    /// the query targets it (see the class remarks).
    /// </summary>
    public static List<AutomationElement> FindAllDescendants(AutomationElement root, ElementQuery query, Func<AutomationElement, bool> match)
    {
        // Windows are always HWNDs (child windows, or owned windows UIA shows under their owner): under a window that has
        // one, Win32 gives the complete answer without reading the UIA tree (which can take seconds on a large form)
        if (query.Kind == ControlKind.Window && HasWindowHandle(root)) return [.. FromWindows(root, match)];

        var result = new List<AutomationElement>();
        if (SkipsTables(root, query)) Walk(root, match, result, firstOnly: false);
        else ReadNodes(root, match, result, firstOnly: false);
        return result;
    }

    /// <summary>
    /// The first descendant matching <paramref name="condition"/> or, outside tables when UIA finds none, one found by a
    /// walk with <paramref name="match"/> (the same test, client-side). The inside of tables is left out unless the query
    /// targets it (see the class remarks).
    /// </summary>
    public static AutomationElement? FindFirstDescendant(AutomationElement root, ConditionBase condition, ElementQuery query, Func<AutomationElement, bool> match)
    {
        if (query.Kind == ControlKind.Window && HasWindowHandle(root)) return FromWindows(root, match).FirstOrDefault();

        var result = new List<AutomationElement>();
        if (!SkipsTables(root, query))
        {
            ReadNodes(root, match, result, firstOnly: true);
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

        Walk(root, match, result, firstOnly: true);
        return result.FirstOrDefault();
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
    /// per node, without entering tables, until the first match (<paramref name="firstOnly"/>), the end of the tree or the
    /// end of <see cref="SearchBudget.Remaining"/>.
    /// </summary>
    private static void Walk(AutomationElement root, Func<AutomationElement, bool> match, List<AutomationElement> result, bool firstOnly)
    {
        var budget = SearchBudget.Remaining;
        var elapsed = Stopwatch.StartNew();
        Visit(root);

        // True when the walk is over
        bool Visit(AutomationElement el)
        {
            var left = budget - elapsed.Elapsed;
            if (SearchBudget.IsCancelled) return true;
            if (left <= TimeSpan.Zero)
            {
                SearchBudget.ReportIncomplete("the search stopped at the time limit before it had read the whole window; a longer --timeout reads more");
                return true;
            }
            List<Found> children;
            try
            {
                using (UiaTimeout.Limit(el.Automation, left)) children = Search(el, TreeScope.Children, match);
            }
            catch (Exception ex)
            {
                if (UiaErrors.IsTimeout(ex)) SearchBudget.ReportIncomplete(NoAnswer);
                return false;   // the element may have gone, or it did not answer in time
            }
            foreach (var child in children)
            {
                if (child.Matched)
                {
                    result.Add(child.Element);
                    if (firstOnly) return true;
                }
                if (!child.IsTable && Visit(child.Element)) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Adds the matching descendants of <paramref name="root"/> to <paramref name="result"/> in tree order, entering tables,
    /// with one UIA request per node, until the first match (<paramref name="firstOnly"/>), the end of the tree or the end
    /// of <see cref="SearchBudget.Remaining"/>. Stopping early is reported through <see cref="SearchBudget.Incomplete"/>.
    /// </summary>
    private static void ReadNodes(AutomationElement root, Func<AutomationElement, bool> match, List<AutomationElement> result, bool firstOnly)
    {
        var budget = SearchBudget.Remaining;
        var elapsed = Stopwatch.StartNew();
        var walker = root.Automation.TreeWalkerFactory.GetRawViewWalker();
        var (skip, take) = SearchBudget.Rows;
        var column = SearchBudget.Column;
        var read = 0;
        var stop = false;
        var rootIsTable = IsTable(root);   // before the cache scope: the root's properties are not cached
        using (SearchCache(root.Automation).Activate())
        using (UiaTimeout.Limit(root.Automation, StepTimeout))
            Visit(root, rootIsTable);

        bool OutOfTime()
        {
            if (elapsed.Elapsed < budget) return false;
            SearchBudget.ReportIncomplete(read == 0 && skip > 0
                ? $"the time limit ended while walking to row {skip}, because this table cannot jump to a row (no GridPattern); a longer --timeout gets further"
                : $"the search stopped at the time limit after reading {read} elements, so a table may not have been read to its end; a longer --timeout reads more, and --skip-rows starts further down");
            return true;
        }

        void Visit(AutomationElement el, bool isTable)
        {
            var rows = 0;
            int? fromEnd = isTable && column is not null ? ColumnFromEnd(el, column, walker) : null;
            var child = Step(() => isTable && skip > 0 ? FirstRow(el, skip, walker, () => stop = stop || OutOfTime()) : walker.GetFirstChild(el));
            while (child is not null && !stop)
            {
                if (SearchBudget.IsCancelled || OutOfTime())
                {
                    stop = true;
                    return;
                }
                if (isTable && take > 0 && rows >= take)
                {
                    SearchBudget.ReportIncomplete($"only {take} rows of the table were searched (--max-rows), starting after {skip} rows (--skip-rows)");
                    return;
                }
                var isDataRow = isTable && IsRow(child) && !(rows == 0 && skip == 0 && IsColumnHeaderRow(child, walker));
                if (fromEnd is int k)
                {
                    // Only the cell of the column: nothing else in the row is read or matched
                    if (isDataRow && CellFromEnd(child, k) is { } cell) Accept(cell);
                }
                else
                {
                    Accept(child);
                    if (!stop) Visit(child, IsTable(child));
                }
                if (stop) return;
                if (isDataRow) rows++;
                var current = child;
                child = Step(() => walker.GetNextSibling(current));
            }
        }

        void Accept(AutomationElement el)
        {
            read++;
            if (!match(el)) return;
            result.Add(el);
            if (firstOnly) stop = true;
        }

        AutomationElement? CellFromEnd(AutomationElement row, int fromEnd)
        {
            var cells = new List<AutomationElement>();
            for (var cell = Step(() => walker.GetFirstChild(row)); cell is not null && !stop; )
            {
                cells.Add(cell);
                var current = cell;
                cell = Step(() => walker.GetNextSibling(current));
            }
            return fromEnd < cells.Count ? cells[cells.Count - 1 - fromEnd] : null;
        }

        AutomationElement? Step(Func<AutomationElement?> next)
        {
            try
            {
                return next();
            }
            catch (Exception ex) when (UiaErrors.IsTimeout(ex))
            {
                SearchBudget.ReportIncomplete(NoAnswer);
                stop = true;
                return null;
            }
            catch
            {
                return null;   // the element went away
            }
        }
    }

    /// <summary>
    /// The row <paramref name="skip"/> (0-based) of a table or list, or null when it has fewer rows. GridPattern reaches it
    /// directly, however many rows come before it; a container without one, or a provider whose GridPattern does not
    /// answer, is walked row by row until <paramref name="stopped"/>.
    /// </summary>
    internal static AutomationElement? FirstRow(AutomationElement table, int skip, ITreeWalker walker, Func<bool> stopped)
    {
        try
        {
            AutomationElement? cell = null;
            // Patterns are not in the search's cache
            using (CacheRequest.ForceNoCache())
            {
                if (table.Patterns.Grid.PatternOrDefault is { } grid)
                {
                    if (skip >= grid.RowCount.Value) return null;
                    cell = grid.GetItem(skip, 0);
                }
            }
            if (cell is not null && walker.GetParent(cell) is { } row && !row.Equals(table)) return row;
        }
        catch (Exception ex) when (!UiaErrors.IsTimeout(ex))
        {
            // No usable GridPattern: walk the rows below
        }

        var child = walker.GetFirstChild(table);
        for (var index = 0; child is not null && !stopped(); child = walker.GetNextSibling(child))
        {
            if (!IsRow(child) || (index == 0 && IsColumnHeaderRow(child, walker))) continue;
            if (index == skip) return child;
            index++;
        }
        return null;
    }

    /// <summary>
    /// The position of a column counted from the end of a row. A header row and a data row may start differently (a
    /// DataGridView has a corner cell and a row header in front, a WPF DataGrid neither), but they end with the same columns.
    /// </summary>
    private static int ColumnFromEnd(AutomationElement table, string column, ITreeWalker walker)
    {
        for (var child = walker.GetFirstChild(table); child is not null; child = walker.GetNextSibling(child))
        {
            var kind = Props.Val<ControlType>(child, child.Automation.PropertyLibrary.Element.ControlType);
            if (kind == ControlType.ScrollBar) continue;
            if (kind != ControlType.Header && !IsColumnHeaderRow(child, walker)) break;

            var headers = new List<string>();
            for (var header = walker.GetFirstChild(child); header is not null; header = walker.GetNextSibling(header))
                headers.Add(Props.Ref<string>(header, header.Automation.PropertyLibrary.Element.Name) ?? "");
            var index = headers.IndexOf(column);
            if (index < 0) throw new CliException($"The table has no column \"{column}\". Its headers: {string.Join(", ", headers.Select(h => $"\"{h}\""))}");
            return headers.Count - 1 - index;
        }
        throw new CliException($"--column needs column headers, and this table does not list any: use a selector for the cell instead of --column \"{column}\"");
    }

    /// <summary>Whether a child of a table is one of its rows, not a scroll bar or column header.</summary>
    private static bool IsRow(AutomationElement e) => !IsHeaderOrScrollBar(e);

    private static bool IsHeaderOrScrollBar(AutomationElement e) =>
        Props.Val<ControlType>(e, e.Automation.PropertyLibrary.Element.ControlType) is ControlType.ScrollBar or ControlType.Header or ControlType.HeaderItem;

    /// <summary>
    /// Whether a row holds only headers: a DataGridView lists its column headers as a row before the data. Data rows also
    /// have a header (the row's own), and at least one cell.
    /// </summary>
    private static bool IsColumnHeaderRow(AutomationElement row, ITreeWalker walker)
    {
        try
        {
            var child = walker.GetFirstChild(row);
            if (child is null) return false;
            for (; child is not null; child = walker.GetNextSibling(child))
            {
                if (!IsHeaderOrScrollBar(child)) return false;
            }
            return true;
        }
        catch (Exception ex) when (!UiaErrors.IsTimeout(ex))
        {
            return false;
        }
    }

    private readonly record struct Found(AutomationElement Element, bool Matched, bool IsTable);

    /// <summary>
    /// FindAll with TrueCondition, testing each element while the properties used by selectors are cached (the test
    /// must run inside the CacheRequest scope, otherwise every property read is a cross-process call).
    /// </summary>
    private static List<Found> Search(AutomationElement el, TreeScope scope, Func<AutomationElement, bool> match)
    {
        using (SearchCache(el.Automation).Activate())
        {
            return [.. el.FindAll(scope, TrueCondition.Default).Select(e => new Found(e, match(e), IsTable(e)))];
        }
    }

    private static CacheRequest SearchCache(AutomationBase automation)
    {
        var lib = automation.PropertyLibrary;
        // Full: the elements are returned to callers and searched again (FlaUI's default None leaves them unusable)
        var cr = new CacheRequest { TreeScope = TreeScope.Element, AutomationElementMode = AutomationElementMode.Full };
        cr.Add(lib.Element.Name);
        cr.Add(lib.Element.AutomationId);
        cr.Add(lib.Element.ClassName);
        cr.Add(lib.Element.ControlType);
        cr.Add(lib.Element.ProcessId);
        return cr;
    }

    private static bool IsTable(AutomationElement e) =>
        Props.Val<ControlType>(e, e.Automation.PropertyLibrary.Element.ControlType) is { } t && DataContainers.IsTable(Mapping.ToKind(t));
}
