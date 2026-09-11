using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Engine;

// Action commands that operate on elements
public sealed partial class CommandDispatcher
{
    private IInputDevice Input => _s.Driver.Input;

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
            if (!el.TryInvoke()) throw new CliException($"{label} does not support InvokePattern; use a normal click instead");
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

        BeforeAction(ctx, el, secret ? $"Enter the password in {Friendly(el)}" : $"Type \"{text}\" into {Friendly(el)}");
        EnsureInteractable(ctx, el);

        if (ctx.Call.GetBool("keyboard") || !el.TrySetValue(text))
        {
            el.Focus();
            Input.PressChord([0x11, 0x41]); // Ctrl+A
            Input.PressChord([0x2E]);       // Delete
            if (text.Length > 0) Input.Type(text);
        }

        Input.WaitUntilIdle();
        return $"### Result\nFilled {label} with \"{(secret ? "********" : text)}\"";
    }

    private string TypeText(CommandContext ctx)
    {
        var text = ctx.Call.Require("text");
        var focused = _s.Driver.GetFocusedElement();
        BeforeAction(ctx, focused, $"Type \"{text}\"");
        TryForeground(_s.RequireWindow());
        Input.Type(text);
        Input.WaitUntilIdle();
        return $"### Result\nTyped \"{text}\"";
    }

    private string Press(CommandContext ctx)
    {
        var keys = ctx.Call.Require("keys");
        var vks = KeyParser.Parse(keys);
        var hasTarget = ctx.Call.Get("target") is not null;
        var target = hasTarget ? ResolveArg(ctx) : _s.Driver.GetFocusedElement();

        BeforeAction(ctx, target, $"Press {keys}");
        if (hasTarget)
        {
            EnsureInteractable(ctx, target!);
            target!.Focus();
        }
        else
        {
            TryForeground(_s.RequireWindow());
        }

        Input.PressChord(vks);
        Input.WaitUntilIdle();
        return $"### Result\nPressed {keys}";
    }

    private static readonly HashSet<ControlKind> ItemKinds =
        [ControlKind.ListItem, ControlKind.TabItem, ControlKind.TreeItem, ControlKind.DataItem, ControlKind.MenuItem, ControlKind.RadioButton];

    private string Select(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var item = ctx.Call.Require("item");
        int? index = item.StartsWith('#') && int.TryParse(item[1..], out var ix) ? ix : null;
        var label = Label(el);

        BeforeAction(ctx, el, $"Select \"{item}\" in {Friendly(el)}");
        EnsureInteractable(ctx, el);

        string selected;
        if (el.Kind == ControlKind.ComboBox)
        {
            if (!el.TrySelectComboBoxItem(index is null ? item : null, index, out var s))
                throw new CliException($"Cannot select \"{item}\" in {label}");
            selected = s ?? item;
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
        return $"### Result\nSelected \"{selected}\" in {label}";
    }

    private string SetChecked(CommandContext ctx, bool check)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, check ? $"Check {Friendly(el)}" : $"Uncheck {Friendly(el)}");
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
        if (!el.TryInvoke()) throw new CliException($"{label} does not support InvokePattern");
        Input.WaitUntilIdle();
        return $"### Result\nInvoked {label}";
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
