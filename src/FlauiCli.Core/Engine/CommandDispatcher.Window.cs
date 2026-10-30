using System.Text;
using System.Text.RegularExpressions;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Native;
using FlauiCli.Core.Snapshot;
using FlauiCli.Core.Targeting;

namespace FlauiCli.Core.Engine;

// Window and inspection commands
public sealed partial class CommandDispatcher
{
    /// <summary>
    /// The windows one can switch to: the current window, the application's top-level windows and its dialogs.
    /// Tooltips and unnamed popups (drop-down panes) are top-level windows too, but not ones to switch to.
    /// </summary>
    private IReadOnlyList<IUiElement> WindowList()
    {
        var current = _s.RequireWindow();
        var list = _s.GetTopLevelWindows().Where(IsSwitchable).ToList();
        if (!list.Any(current.Equals)) list.Insert(0, current);
        foreach (var child in ChildWindows())
        {
            if (!list.Any(child.Equals)) list.Add(child);
        }
        return list;
    }

    private static bool IsSwitchable(IUiElement w)
    {
        try
        {
            return w.Kind != ControlKind.ToolTip && (w.Kind == ControlKind.Window || w.Name.Length > 0);
        }
        catch
        {
            return false;   // The popup closed meanwhile
        }
    }

    private string Windows(CommandContext ctx)
    {
        var current = _s.RequireWindow();
        var sb = new StringBuilder("### Windows\n");
        var list = WindowList();
        for (var i = 0; i < list.Count; i++)
        {
            var w = list[i];
            sb.AppendLine($"- [{i}] {Label(w)}{(IsMinimizedSafe(w) ? " (minimized)" : "")}{(w.Equals(current) ? " (current)" : "")}");
        }
        return sb.ToString();
    }

    private string SwitchWindow(CommandContext ctx)
    {
        ActivateWindow(FindWindow(ctx, ctx.Call.Require("window")));
        return $"### Result\nSwitched window\n{WindowSection()}";
    }

    /// <summary>A window by index (as listed by windows), ref or title.</summary>
    private IUiElement FindWindow(CommandContext ctx, string key)
    {
        var list = WindowList();
        if (int.TryParse(key, out var index))
            return index >= 0 && index < list.Count ? list[index] : throw new CliException($"Window index out of range: {index} ({list.Count} windows)");
        if (Selector.LooksLikeRef(key)) return Resolve(ctx, key);
        return list.FirstOrDefault(w => string.Equals(w.Name, key, StringComparison.OrdinalIgnoreCase))
               ?? list.FirstOrDefault(w => w.Name.Contains(key, StringComparison.OrdinalIgnoreCase))
               ?? FindTopLevelByTitle(key, _s.CurrentWindow?.ProcessId)
               ?? throw new CliException($"Window not found: {key}");
    }

    /// <summary>
    /// Closes one window: WindowPattern first, then WM_CLOSE (what the title bar close button sends), which also
    /// closes WinForms dialogs that have no Cancel button. When the current window closes, the session switches back
    /// to the window that was current before it, or else to another window of the application.
    /// </summary>
    private string CloseWindow(CommandContext ctx)
    {
        var current = _s.RequireWindow();
        var target = ctx.Call.Get("window") is { } key ? FindWindow(ctx, key) : current;
        var label = Label(target);
        var isCurrent = target.Equals(current);
        // Back to the window the dialog was opened from (an MDI child rather than the MDI parent), else another window
        var fallback = isCurrent
            ? _s.PreviousWindow(target)
              ?? _s.GetTopLevelWindows().FirstOrDefault(w => !w.Equals(target) && w.Kind == ControlKind.Window && w.Name.Length > 0)
            : null;
        var hwnd = target.WindowHandle;
        var timeout = ctx.Timeout;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var closing = false;
        try { closing = target.TryClose(); }
        catch { /* No WindowPattern and no close button: WM_CLOSE below */ }

        if (!Poll(closing ? Math.Min(timeout, 1500) : 0, () => !target.IsAlive))
        {
            var posted = false;
            try { posted = NativeMethods.PostClose(hwnd); }
            catch { /* Not available */ }
            var remaining = (int)Math.Max(0, timeout - sw.ElapsedMilliseconds);
            if (!(posted || closing) || !Poll(remaining, () => !target.IsAlive))
                throw new CliException($"{label} did not close within {timeout}ms (it may be asking for confirmation; check with snapshot or screenshot)");
        }

        if (isCurrent && fallback is not null) ActivateWindow(fallback);
        return $"### Result\nClosed {label}\n{WindowSection()}";
    }

    private string Focus(CommandContext ctx)
    {
        if (ctx.Call.Get("target") is { } raw)
        {
            var el = Resolve(ctx, raw);
            BeforeAction(ctx, el, $"Focus {Friendly(el)}");
            EnsureInteractable(ctx, el);
            el.Focus();
            return $"### Result\n{Label(el)} has focus";
        }

        var window = _s.RequireWindow();
        window.SetForeground();
        return $"### Result\nBrought the window to the foreground\n{WindowSection()}";
    }

    /// <summary>Minimized windows report no content (and off-screen bounds around -32000).</summary>
    private static bool IsMinimized(IUiElement window) =>
        NativeMethods.IsMinimized(window.WindowHandle) || window.Bounds is { X: <= -32000, Y: <= -32000 };

    private static bool IsMinimizedSafe(IUiElement window)
    {
        try { return IsMinimized(window); }
        catch { return false; }    // The window closed meanwhile
    }

    /// <summary>Whether another window of the same application (a dialog) is on screen.</summary>
    private static bool HasOtherWindowOnScreen(IUiElement window) =>
        NativeMethods.VisibleTopLevelWindows(window.ProcessId).Any(w =>
            w.Handle != window.WindowHandle
            && !w.ClassName.Contains("tooltips_class32", StringComparison.OrdinalIgnoreCase)
            && !NativeMethods.IsMinimized(w.Handle)
            && NativeMethods.TryGetVisibleFrame(w.Handle, out var frame) && frame.Width > 0 && frame.Height > 0);

    private string SetWindowState(CommandContext ctx, WindowStateKind state)
    {
        var window = _s.RequireWindow();
        bool supported;
        try
        {
            supported = window.TrySetWindowState(state);
        }
        catch (InvalidOperationException ex) when (!window.IsEnabled)
        {
            throw new CliException($"The window \"{window.Name}\" is disabled, so its state cannot be changed. A dialog of the application is probably open; act on it or close it first", ex);
        }
        if (!supported) throw new CliException("This window does not support WindowPattern, so its state cannot be changed");
        return $"### Result\nWindow {state switch { WindowStateKind.Maximized => "maximized", WindowStateKind.Minimized => "minimized", _ => "restored" }}";
    }

    private string Resize(CommandContext ctx)
    {
        var w = ctx.Call.GetInt("width") ?? throw new CliException("Missing width");
        var h = ctx.Call.GetInt("height") ?? throw new CliException("Missing height");
        var window = _s.RequireWindow();
        window.TrySetWindowState(WindowStateKind.Normal);
        if (!window.TryResize(w, h)) throw new CliException("This window cannot be resized");
        return $"### Result\nWindow resized to {w}x{h}";
    }

    private string MoveWindow(CommandContext ctx)
    {
        var x = ctx.Call.GetInt("x") ?? throw new CliException("Missing x");
        var y = ctx.Call.GetInt("y") ?? throw new CliException("Missing y");
        var window = _s.RequireWindow();
        window.TrySetWindowState(WindowStateKind.Normal);
        if (!window.TryMove(x, y)) throw new CliException("This window cannot be moved");
        return $"### Result\nWindow moved to ({x}, {y})";
    }

    private string TakeSnapshot(CommandContext ctx)
    {
        var window = _s.RequireWindow();
        var root = ctx.Call.Get("root") is { } r ? Resolve(ctx, r) : window;
        var options = new SnapshotOptions(
            ctx.Call.GetInt("depth") ?? ctx.Config.Snapshot.Depth,
            ctx.Call.GetBool("boxes"),
            ctx.Call.GetBool("all") || ctx.Config.Snapshot.IncludeOffscreen,
            MaxRows(ctx));
        var text = BuildSnapshot(root, options, includePopups: root.Equals(window));
        // Some minimized windows (UWP) expose no content; WinForms ones keep it, and then there is nothing to say
        if (root.Equals(window) && IsMinimized(window) && window.FindFirst(ElementQuery.Any) is null)
            text += "\n# The window is minimized, so its content is not listed. Run restore first.";

        if (ctx.Call.Get("filename") is { } file)
        {
            var path = ctx.Call.ResolvePath(file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            ctx.Data["path"] = path;
            return $"### Result\nSnapshot saved: {ctx.Relative(path)}";
        }

        return $"{WindowSection()}### Snapshot\n```yaml\n{text}\n```";
    }

    /// <summary>Items read from each table or list: <c>--max-rows</c>, else <c>snapshot.maxRows</c> in the config file (0 = all).</summary>
    private static int MaxRows(CommandContext ctx)
    {
        var rows = ctx.Call.GetInt("max-rows") ?? ctx.Config.Snapshot.MaxRows;
        return rows >= 0 ? rows : throw new CliException($"--max-rows must be 0 (all rows) or more: {rows}");
    }

    private string Find(CommandContext ctx)
    {
        const int limit = 50;
        var text = ctx.Call.Require("text");
        Regex? re = null;
        if (ctx.Call.GetBool("regex"))
        {
            try { re = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)); }
            catch (ArgumentException ex) { throw new CliException($"Invalid regular expression: {ex.Message}", ex); }
        }

        bool Match(string? s) => !string.IsNullOrEmpty(s) && (re?.IsMatch(s) ?? s.Contains(text, StringComparison.OrdinalIgnoreCase));

        var matches = _s.SearchRoots()
            .SelectMany(root => root.CaptureTree(maxRows: MaxRows(ctx)).DescendantsAndSelf())
            .Where(n => Match(n.Name) || Match(n.AutomationId) || Match(n.Value))
            .ToList();

        if (matches.Count == 0) return $"### Result\nNo element matches \"{text}\"";

        var sb = new StringBuilder($"### Result\nFound {matches.Count} element(s){(matches.Count > limit ? $" (showing the first {limit})" : "")}\n");
        foreach (var n in matches.Take(limit))
            sb.AppendLine("- " + SnapshotFormatter.Line(n, _s.Refs.Assign(n), boxes: false));
        return sb.ToString();
    }

    private string Inspect(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var b = el.Bounds;
        var sb = new StringBuilder("### Element\n");
        sb.AppendLine($"- Ref: {_s.Refs.GetOrAssign(el)}");
        sb.AppendLine($"- Suggested selector: {StableSelector(el)}");
        sb.AppendLine($"- ControlType: {el.Kind}");
        sb.AppendLine($"- Name: {el.Name}");
        sb.AppendLine($"- AutomationId: {el.AutomationId}");
        sb.AppendLine($"- ClassName: {el.ClassName}");
        sb.AppendLine($"- FrameworkId: {el.FrameworkId}");
        sb.AppendLine($"- IsEnabled: {el.IsEnabled}");
        sb.AppendLine($"- IsOffscreen: {el.IsOffscreen}");
        sb.AppendLine($"- HasKeyboardFocus: {el.HasFocus}");
        sb.AppendLine($"- BoundingRectangle: {b.X},{b.Y},{b.Width},{b.Height}");
        sb.AppendLine($"- ProcessId: {el.ProcessId}");
        if (el.Value is { } v) sb.AppendLine($"- Value: {v}{(el.IsReadOnly == true ? " (read-only)" : "")}");
        if (el.Toggle is { } t) sb.AppendLine($"- ToggleState: {t}");
        if (el.Expand is { } e) sb.AppendLine($"- ExpandCollapseState: {e}");
        if (el.IsSelected is { } s) sb.AppendLine($"- IsSelected: {s}");
        foreach (var name in new[] { "HelpText", "ItemStatus", "LocalizedControlType", "AcceleratorKey", "AccessKey" })
        {
            if (el.GetProperty(name) is { Length: > 0 } pv) sb.AppendLine($"- {name}: {pv}");
        }
        sb.AppendLine($"- Supported patterns: {string.Join(", ", el.SupportedPatterns)}");
        return sb.ToString();
    }
}
