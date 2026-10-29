using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Engine;

// Action commands that operate on elements
public sealed partial class CommandDispatcher
{
    private IInputDevice Input => _input ??= new CheckedInput(_s.Driver, () => _unlockWaitMs, MinimizedWindowReason);

    /// <summary>Why simulated input cannot reach the current window: it is minimized, so its controls are off screen.</summary>
    private string? MinimizedWindowReason()
    {
        try
        {
            return _s.CurrentWindow is { } w && IsMinimized(w)
                ? $"The window \"{w.Name}\" is minimized, so mouse and keyboard input cannot reach it. Run restore first"
                : null;
        }
        catch
        {
            return null;    // The window closed meanwhile
        }
    }

    private CheckedInput? _input;

    private static MouseButtonKind ParseButton(string? value) => (value ?? "left").ToLowerInvariant() switch
    {
        "left" => MouseButtonKind.Left,
        "right" => MouseButtonKind.Right,
        "middle" => MouseButtonKind.Middle,
        _ => throw new CliException($"Unknown mouse button: {value} (use left, right or middle)"),
    };

    private string Click(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var button = ParseButton(ctx.Call.Get("button"));
        var dbl = ctx.Call.GetBool("double");
        var label = Label(el);

        BeforeAction(ctx, el, dbl ? $"Double-click {Friendly(el)}"
            : button == MouseButtonKind.Right ? $"Right-click {Friendly(el)}"
            : $"Click {Friendly(el)}");

        if (ctx.Call.GetBool("invoke"))
        {
            EnsureInteractable(ctx, el);
            InvokeBounded(ctx, el, label, $"{label} does not support InvokePattern; use a normal click instead");
        }
        else
        {
            EnsureInteractable(ctx, el);
            var point = el.ClickPoint();
            if (dbl) Input.DoubleClick(point, button);
            else Input.Click(point, button);
        }

        Input.WaitUntilIdle();
        return $"### Result\n{(dbl ? "Double-clicked" : button == MouseButtonKind.Right ? "Right-clicked" : "Clicked")} {label}";
    }

    private string DoubleClick(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, $"Double-click {Friendly(el)}");
        EnsureInteractable(ctx, el);
        Input.DoubleClick(el.ClickPoint(), MouseButtonKind.Left);
        Input.WaitUntilIdle();
        return $"### Result\nDouble-clicked {label}";
    }

    private string Hover(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        BeforeAction(ctx, el, $"Hover over {Friendly(el)}");
        EnsureInteractable(ctx, el);
        Input.MoveTo(el.ClickPoint());
        return $"### Result\nMoved the mouse over {Label(el)}";
    }

    private string Fill(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        if (!ctx.Call.Has("text")) throw new CliException("fill is missing the required argument <text>");
        var text = ctx.Call.Get("text") ?? "";
        var label = Label(el);
        var secret = el.IsPassword;
        // Password fields: mask the value before it reaches documents, recordings and the result (same as the input recorder)
        if (secret) ctx.Call.Set("text", CommandCall.Masked);

        EnsureInteractable(ctx, el);

        if (ctx.Call.GetBool("keyboard") || !el.TrySetValue(text))
        {
            el.Focus();
            Input.PressChord([0x11, 0x41]); // Ctrl+A
            Input.PressChord([0x2E]);       // Delete
            if (text.Length > 0) Input.Type(text);
        }

        Input.WaitUntilIdle();
        AfterAction(ctx, el, secret ? $"Enter the password in {Friendly(el)}" : $"Type \"{text}\" into {Friendly(el)}");
        return $"### Result\nFilled {label} with \"{(secret ? CommandCall.Masked : text)}\"";
    }

    private string TypeText(CommandContext ctx)
    {
        var text = ctx.Call.Require("text");
        var focused = _s.Driver.GetFocusedElement();
        ForegroundForKeys(focused);
        Input.Type(text);
        Input.WaitUntilIdle();
        AfterAction(ctx, focused, $"Type \"{text}\"");
        return $"### Result\nTyped \"{text}\"";
    }

    private string Press(CommandContext ctx)
    {
        var keys = ctx.Call.Require("keys");
        var vks = KeyParser.Parse(keys);
        var repeat = ctx.Call.GetInt("repeat") ?? 1;
        if (repeat is < 1 or > 100) throw new CliException($"--repeat must be between 1 and 100: {repeat}");
        var times = repeat > 1 ? $" {repeat} times" : "";
        var hasTarget = ctx.Call.Get("target") is not null;
        if (IsInvokePending && !hasTarget)
        {
            // UI Automation is blocked by the pending invoke; send the keys to the foreground window (the dialog)
            for (var i = 0; i < repeat; i++)
            {
                Input.PressChord(vks);
                Input.WaitUntilIdle();
            }
            return $"### Result\nPressed {keys}{times} (sent to the foreground window; UI Automation is waiting for {_pendingInvoke!.Label})";
        }
        var target = hasTarget ? ResolveArg(ctx) : _s.Driver.GetFocusedElement();

        BeforeAction(ctx, target, $"Press {keys}{times}");
        if (hasTarget) EnsureInteractable(ctx, target!);
        else ForegroundForKeys(target);

        for (var i = 0; i < repeat; i++)
        {
            // An application may move the focus while handling a key (WinForms forms often jump to the next field
            // from SelectedIndexChanged): with a target, every press goes back to it
            if (hasTarget) target!.Focus();
            Input.PressChord(vks);
            Input.WaitUntilIdle();
        }

        return $"### Result\nPressed {keys}{times}" + (hasTarget ? "" : FocusMovedNote(target));
    }

    /// <summary>
    /// Brings the current window forward before keys are sent without a target, unless the focus is already in the
    /// application: in a dialog opened by a plain click the current window is still the parent, and bringing it
    /// forward would send the keys there instead of to the dialog.
    /// </summary>
    private void ForegroundForKeys(IUiElement? focused)
    {
        var window = _s.RequireWindow();
        try
        {
            if (focused is not null && focused.ProcessId == window.ProcessId) return;
        }
        catch
        {
            // The focused element went away; bring the window forward
        }
        TryForeground(window);
    }

    /// <summary>
    /// When the application moved the focus away from the element that had it before the key press, says so: later
    /// presses without a target go to the new element, which looks like the keys stopped working.
    /// </summary>
    private string FocusMovedNote(IUiElement? before)
    {
        // A key that closed a dialog takes the focus back to the window that opened it, as expected
        if (before is null || !before.IsAlive) return "";
        IUiElement? after;
        try { after = _s.Driver.GetFocusedElement(); }
        catch { return ""; }
        if (after is null || after.Equals(before)) return "";
        var r = _s.Refs.GetOrAssign(before);
        return $"\nThe focus moved from {Label(before)} to {Label(after)}; further keys go there. " +
               $"To keep pressing keys in {r}, use press <keys> --target {r}";
    }

    private static readonly HashSet<ControlKind> ItemKinds =
        [ControlKind.ListItem, ControlKind.TabItem, ControlKind.TreeItem, ControlKind.DataItem, ControlKind.MenuItem, ControlKind.RadioButton];

    private string Select(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var item = ctx.Call.Require("item");
        int? index = item.StartsWith('#') && int.TryParse(item[1..], out var ix) ? ix : null;
        var label = Label(el);

        EnsureInteractable(ctx, el);
        if (ctx.Call.GetBool("keyboard")) return SelectWithKeyboard(ctx, el, item, index, label);

        string selected;
        if (el.Kind == ControlKind.ComboBox)
        {
            var before = el.Value;
            if (!el.TrySelectComboBoxItem(index is null ? item : null, index, out var s))
                throw new CliException($"Cannot select \"{item}\" in {label}");
            selected = s ?? item;
            Input.WaitUntilIdle();
            EnsureComboBoxValue(el, before, selected, label);
        }
        else
        {
            var items = el.FindAll(ElementQuery.Any).Where(e => ItemKinds.Contains(e.Kind)).ToList();
            var target = index is int i
                ? items.ElementAtOrDefault(i)
                : items.FirstOrDefault(e => e.Name == item) ?? items.FirstOrDefault(e => e.Name.Contains(item, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                var names = string.Join(", ", items.Select(e => e.Name).Where(n => n.Length > 0).Take(20));
                throw new CliException($"Item \"{item}\" not found in {label}. Available items: {(names.Length > 0 ? names : "(none)")}");
            }
            if (target.IsOffscreen) target.TryScrollIntoView();
            if (!target.TrySelectItem()) Input.Click(target.ClickPoint(), MouseButtonKind.Left);
            selected = target.Name;
        }

        Input.WaitUntilIdle();
        AfterAction(ctx, el, $"Select \"{item}\" in {Friendly(el)}");
        return $"### Result\nSelected \"{selected}\" in {label}";
    }

    /// <summary>
    /// Selects with the arrow keys, as a person would. Selecting through UI Automation sets the value without the
    /// application's own selection events (WinForms SelectedIndexChanged); key presses go through the control's normal
    /// input handling, so the application reacts to them. The control is focused again before every key, in case the
    /// application moves the focus when the selection changes.
    /// </summary>
    private string SelectWithKeyboard(CommandContext ctx, IUiElement el, string item, int? index, string label)
    {
        if (el.Kind is not (ControlKind.ComboBox or ControlKind.List))
            throw new CliException($"select --keyboard works on combo boxes and lists; {label} is a {el.Kind.Role()}");

        // Combo boxes: the item texts and the current value. Lists: the item elements and their selection state.
        var comboItems = el.Kind == ControlKind.ComboBox ? el.TryGetComboBoxItems() : null;
        var listItems = comboItems is null ? el.FindAll(ElementQuery.Any).Where(e => e.Kind == ControlKind.ListItem).ToList() : [];
        List<string> names = comboItems is not null ? [.. comboItems] : [.. listItems.Select(e => e.Name)];
        if (names.Count == 0) throw new CliException($"Cannot read the items of {label}; select without --keyboard instead");

        var target = index ?? IndexOf(names, item);
        if (target < 0 || target >= names.Count)
        {
            var available = string.Join(", ", names.Where(n => n.Length > 0).Take(20));
            throw new CliException($"Item \"{item}\" not found in {label}. Available items: {available}");
        }
        var wanted = names[target];

        int Current()
        {
            var selected = listItems.FindIndex(e => e.IsSelected == true);
            return selected >= 0 || el.Value is not { } v ? selected : names.FindIndex(n => n.Trim() == v.Trim());
        }

        var delta = target - Current();
        if (delta == 0)
        {
            AfterAction(ctx, el, $"Select \"{wanted}\" in {Friendly(el)}");
            return $"### Result\n\"{wanted}\" is already selected in {label}; no key was pressed, so the application saw no selection change";
        }

        var key = delta > 0 ? "Down" : "Up";
        var vks = KeyParser.Parse(key);
        for (var i = 0; i < Math.Abs(delta); i++)
        {
            el.Focus();
            Input.PressChord(vks);
            Input.WaitUntilIdle();
        }

        if (!Poll(1000, () => Current() == target, intervalMs: 100))
        {
            var now = Current();
            throw new CliException(
                $"Pressed {key} {Math.Abs(delta)} time(s) in {label}, but \"{(now >= 0 ? names[now] : el.Value)}\" is selected instead of \"{wanted}\"");
        }

        AfterAction(ctx, el, $"Select \"{wanted}\" in {Friendly(el)}");
        return $"### Result\nSelected \"{wanted}\" in {label} with the keyboard ({key} × {Math.Abs(delta)})";
    }

    private static int IndexOf(List<string> names, string name)
    {
        var exact = names.FindIndex(n => n == name);
        return exact >= 0 ? exact : names.FindIndex(n => n.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Some combo boxes accept the SelectionItemPattern call without applying it (for example the WinForms
    /// ToolStripComboBox): their value stays what it was. In that case the value is set through ValuePattern (as
    /// fill does); when that is not possible either, the command fails instead of reporting a selection that did
    /// not happen. Combo boxes whose value cannot be read are trusted.
    /// </summary>
    private void EnsureComboBoxValue(IUiElement el, string? before, string selected, string label)
    {
        bool Applied() => el.Value is not { } v || v.Trim() == selected.Trim() || v != before;
        if (before is null || before.Trim() == selected.Trim() || Poll(500, Applied, intervalMs: 100)) return;

        if (el.TrySetValue(selected))
        {
            Input.WaitUntilIdle();
            if (Poll(500, Applied, intervalMs: 100)) return;
        }

        throw new CliException(
            $"Selected \"{selected}\" in {label}, but the selection did not take effect: the value is still \"{before}\". " +
            $"Try fill {_s.Refs.GetOrAssign(el)} \"{selected}\" instead");
    }

    private string SetChecked(CommandContext ctx, bool check)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        EnsureInteractable(ctx, el);

        var desired = check ? ToggleValue.On : ToggleValue.Off;
        if (el.Toggle is not null)
        {
            // A three-state check box may need two toggles
            for (var i = 0; i < 3 && el.Toggle != desired; i++)
            {
                if (!el.TryToggle()) break;
            }
            if (el.Toggle != desired) throw new CliException($"Cannot set {label} to {(check ? "checked" : "unchecked")}");
        }
        else if (el.IsSelected is not null)
        {
            if (!check) throw new CliException($"{label} is a radio button and cannot be unchecked (select another option instead)");
            if (el.IsSelected != true && !el.TrySelectItem()) throw new CliException($"Cannot select {label}");
        }
        else
        {
            throw new CliException($"{label} is not a check box or toggle button");
        }

        AfterAction(ctx, el, check ? $"Check {Friendly(el)}" : $"Uncheck {Friendly(el)}");
        return $"### Result\n{(check ? "Checked" : "Unchecked")} {label}";
    }

    private string ExpandCollapse(CommandContext ctx, bool expand)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, expand ? $"Expand {Friendly(el)}" : $"Collapse {Friendly(el)}");
        EnsureInteractable(ctx, el);
        var ok = expand ? el.TryExpand() : el.TryCollapse();
        if (!ok) throw new CliException($"{label} does not support ExpandCollapsePattern");
        Input.WaitUntilIdle();
        return $"### Result\n{(expand ? "Expanded" : "Collapsed")} {label}";
    }

    private string Invoke(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, $"Invoke {Friendly(el)}");
        EnsureInteractable(ctx, el);
        InvokeBounded(ctx, el, label, $"{label} does not support InvokePattern");
        Input.WaitUntilIdle();
        return $"### Result\nInvoked {label}";
    }

    /// <summary>
    /// Calls InvokePattern on a separate thread and waits at most the command timeout.
    /// For WinForms controls, Invoke does not return until a modal dialog opened by the handler
    /// (Form.ShowDialog) is closed. While it is pending, UI Automation calls to the application queue
    /// behind it, so the command fails and later commands fail fast (see <see cref="ThrowIfInvokePending"/>)
    /// instead of hanging until the dialog is closed.
    /// </summary>
    private void InvokeBounded(CommandContext ctx, IUiElement el, string label, string notSupported)
    {
        var task = Task.Factory.StartNew<bool>(el.TryInvoke, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (Task.WhenAny(task, Task.Delay(ctx.Timeout)).GetAwaiter().GetResult() == task)
        {
            if (!task.GetAwaiter().GetResult()) throw new CliException(notSupported);
            return;
        }

        // Observe a late failure so it does not surface as an unobserved task exception
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _pendingInvoke = new PendingInvoke(task, label);
        throw new CliException(
            $"InvokePattern on {label} did not return within {ctx.Timeout}ms: it probably opened a modal dialog " +
            "and only returns when the dialog closes. Until then UI Automation calls to this application wait, so other " +
            "commands fail; press Escape / press Enter (without --target) still work. Use a plain click (without --invoke) " +
            "for controls that open a dialog.");
    }

    private string Scroll(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var direction = (ctx.Call.Get("dir") ?? "down").ToLowerInvariant() switch
        {
            "up" => ScrollDirection.Up,
            "down" => ScrollDirection.Down,
            "left" => ScrollDirection.Left,
            "right" => ScrollDirection.Right,
            var d => throw new CliException($"Unknown scroll direction: {d} (use up, down, left or right)"),
        };
        var amount = Math.Max(1, ctx.Call.GetInt("amount") ?? 3);
        BeforeAction(ctx, el, $"Scroll {Friendly(el)}");
        TryForeground(_s.RequireWindow());

        // Prefer ScrollPattern; fall back to the mouse wheel when it is not supported
        if (el.TryScroll(direction))
        {
            for (var i = 1; i < amount; i++) el.TryScroll(direction);
        }
        else
        {
            Input.MoveTo(el.ClickPoint());
            var horizontal = direction is ScrollDirection.Left or ScrollDirection.Right;
            var sign = direction is ScrollDirection.Up or ScrollDirection.Right ? 1 : -1;
            Input.Scroll(sign * amount, horizontal);
        }

        Input.WaitUntilIdle();
        return $"### Result\nScrolled {Label(el)} {direction.ToString().ToLowerInvariant()} {amount} time(s)";
    }

    private string Drag(CommandContext ctx)
    {
        var source = ResolveArg(ctx, "source");
        var dest = ResolveArg(ctx, "dest");
        BeforeAction(ctx, source, $"Drag {Friendly(source)} to {Friendly(dest)}");
        EnsureInteractable(ctx, source);
        Input.Drag(source.ClickPoint(), dest.ClickPoint());
        Input.WaitUntilIdle();
        return $"### Result\nDragged {Label(source)} to {Label(dest)}";
    }
}
