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
/// </summary>
internal static class UiaSearch
{
    /// <summary>A search returning at least this many elements may have been truncated.</summary>
    public const int TruncationThreshold = 500;

    /// <summary>
    /// The descendants of <paramref name="root"/> matching <paramref name="match"/> (the client-side form of
    /// <paramref name="query"/>). Complete when the tree is small; on a truncated tree, the matches UIA found in tree
    /// order, or when it found none, those of a level-by-level search.
    /// </summary>
    public static List<AutomationElement> FindAllDescendants(AutomationElement root, ElementQuery query, Func<AutomationElement, bool> match)
    {
        // Windows are always HWNDs (child windows, or owned windows UIA shows under their owner): under a window that has
        // one, Win32 gives the complete answer without reading the UIA tree (which can take seconds on a large form)
        if (query.Kind == ControlKind.Window && HasWindowHandle(root)) return [.. FromWindows(root, match)];

        var all = Search(root, TreeScope.Descendants, match);
        var result = all.Where(x => x.Matched).Select(x => x.Element).ToList();
        if (all.Count >= TruncationThreshold && result.Count == 0) Collect(root, match, result, firstOnly: false);   // breadth-first order
        return result;
    }

    /// <summary>
    /// The first descendant matching <paramref name="condition"/> (UIA FindFirst) or, when UIA finds none, one found by
    /// a level-by-level search with <paramref name="match"/> (the same test, client-side).
    /// </summary>
    public static AutomationElement? FindFirstDescendant(AutomationElement root, ConditionBase condition, ElementQuery query, Func<AutomationElement, bool> match)
    {
        if (query.Kind == ControlKind.Window && HasWindowHandle(root)) return FromWindows(root, match).FirstOrDefault();
        if (root.FindFirstDescendant(condition) is { } found) return found;
        var result = new List<AutomationElement>();
        Collect(root, match, result, firstOnly: true);
        return result.FirstOrDefault();
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
        while (queue.Count > 0 && elapsed.Elapsed <= budget)
        {
            List<(AutomationElement Element, bool Matched)> children;
            try { children = Search(queue.Dequeue(), TreeScope.Children, match); }
            catch { continue; }   // the element may have gone
            foreach (var (child, matched) in children)
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

    /// <summary>
    /// FindAll with TrueCondition, testing each element while the properties used by selectors are cached (the test
    /// must run inside the CacheRequest scope, otherwise every property read is a cross-process call).
    /// </summary>
    private static List<(AutomationElement Element, bool Matched)> Search(AutomationElement el, TreeScope scope, Func<AutomationElement, bool> match)
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
            return [.. el.FindAll(scope, TrueCondition.Default).Select(e => (e, match(e)))];
        }
    }
}
