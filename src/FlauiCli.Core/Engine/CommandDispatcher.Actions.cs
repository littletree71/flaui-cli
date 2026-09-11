using FlauiCli.Core.Abstractions;

namespace FlauiCli.Core.Engine;

// 操作元素的動作指令
public sealed partial class CommandDispatcher
{
    private IInputDevice Input => _s.Driver.Input;

    private static MouseButtonKind ParseButton(string? value) => (value ?? "left").ToLowerInvariant() switch
    {
        "left" => MouseButtonKind.Left,
        "right" => MouseButtonKind.Right,
        "middle" => MouseButtonKind.Middle,
        _ => throw new CliException($"未知的滑鼠按鍵：{value}（可用：left, right, middle）"),
    };

    private string Click(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var button = ParseButton(ctx.Call.Get("button"));
        var dbl = ctx.Call.GetBool("double");
        var label = Label(el);

        BeforeAction(ctx, el, dbl ? $"雙擊{Friendly(el)}"
            : button == MouseButtonKind.Right ? $"在{Friendly(el)}上按右鍵"
            : $"點擊{Friendly(el)}");

        if (ctx.Call.GetBool("invoke"))
        {
            EnsureInteractable(ctx, el);
            if (!el.TryInvoke()) throw new CliException($"{label} 不支援 InvokePattern，請改用一般點擊");
        }
        else
        {
            EnsureInteractable(ctx, el);
            var point = el.ClickPoint();
            if (dbl) Input.DoubleClick(point, button);
            else Input.Click(point, button);
        }

        Input.WaitUntilIdle();
        return $"### Result\n已{(dbl ? "雙擊" : button == MouseButtonKind.Right ? "右鍵點擊" : "點擊")} {label}";
    }

    private string DoubleClick(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, $"雙擊{Friendly(el)}");
        EnsureInteractable(ctx, el);
        Input.DoubleClick(el.ClickPoint(), MouseButtonKind.Left);
        Input.WaitUntilIdle();
        return $"### Result\n已雙擊 {label}";
    }

    private string Hover(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        BeforeAction(ctx, el, $"將滑鼠移到{Friendly(el)}");
        EnsureInteractable(ctx, el);
        Input.MoveTo(el.ClickPoint());
        return $"### Result\n滑鼠已移到 {Label(el)}";
    }

    private string Fill(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        if (!ctx.Call.Has("text")) throw new CliException("fill 缺少必要參數 <text>");
        var text = ctx.Call.Get("text") ?? "";
        var label = Label(el);
        var secret = el.IsPassword;

        BeforeAction(ctx, el, secret ? $"在{Friendly(el)}輸入密碼" : $"在{Friendly(el)}輸入「{text}」");
        EnsureInteractable(ctx, el);

        if (ctx.Call.GetBool("keyboard") || !el.TrySetValue(text))
        {
            el.Focus();
            Input.PressChord([0x11, 0x41]); // Ctrl+A
            Input.PressChord([0x2E]);       // Delete
            if (text.Length > 0) Input.Type(text);
        }

        Input.WaitUntilIdle();
        return $"### Result\n已在 {label} 填入「{(secret ? "********" : text)}」";
    }

    private string TypeText(CommandContext ctx)
    {
        var text = ctx.Call.Require("text");
        var focused = _s.Driver.GetFocusedElement();
        BeforeAction(ctx, focused, $"輸入「{text}」");
        TryForeground(_s.RequireWindow());
        Input.Type(text);
        Input.WaitUntilIdle();
        return $"### Result\n已輸入「{text}」";
    }

    private string Press(CommandContext ctx)
    {
        var keys = ctx.Call.Require("keys");
        var vks = KeyParser.Parse(keys);
        var hasTarget = ctx.Call.Get("target") is not null;
        var target = hasTarget ? ResolveArg(ctx) : _s.Driver.GetFocusedElement();

        BeforeAction(ctx, target, $"按下 {keys}");
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
        return $"### Result\n已按下 {keys}";
    }

    private static readonly HashSet<ControlKind> ItemKinds =
        [ControlKind.ListItem, ControlKind.TabItem, ControlKind.TreeItem, ControlKind.DataItem, ControlKind.MenuItem, ControlKind.RadioButton];

    private string Select(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var item = ctx.Call.Require("item");
        int? index = item.StartsWith('#') && int.TryParse(item[1..], out var ix) ? ix : null;
        var label = Label(el);

        BeforeAction(ctx, el, $"在{Friendly(el)}選擇「{item}」");
        EnsureInteractable(ctx, el);

        string selected;
        if (el.Kind == ControlKind.ComboBox)
        {
            if (!el.TrySelectComboBoxItem(index is null ? item : null, index, out var s))
                throw new CliException($"無法在 {label} 選取「{item}」");
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
                var names = string.Join("、", items.Select(e => e.Name).Where(n => n.Length > 0).Take(20));
                throw new CliException($"在 {label} 找不到項目「{item}」。可用項目：{(names.Length > 0 ? names : "（無）")}");
            }
            if (target.IsOffscreen) target.TryScrollIntoView();
            if (!target.TrySelectItem()) Input.Click(target.ClickPoint(), MouseButtonKind.Left);
            selected = target.Name;
        }

        Input.WaitUntilIdle();
        return $"### Result\n已在 {label} 選取「{selected}」";
    }

    private string SetChecked(CommandContext ctx, bool check)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, check ? $"勾選{Friendly(el)}" : $"取消勾選{Friendly(el)}");
        EnsureInteractable(ctx, el);

        var desired = check ? ToggleValue.On : ToggleValue.Off;
        if (el.Toggle is not null)
        {
            // 三態核取方塊可能需要切換兩次
            for (var i = 0; i < 3 && el.Toggle != desired; i++)
            {
                if (!el.TryToggle()) break;
            }
            if (el.Toggle != desired) throw new CliException($"無法將 {label} 設為{(check ? "勾選" : "未勾選")}");
        }
        else if (el.IsSelected is not null)
        {
            if (!check) throw new CliException($"{label} 是選項按鈕，無法取消勾選（請改選其他選項）");
            if (el.IsSelected != true && !el.TrySelectItem()) throw new CliException($"無法選取 {label}");
        }
        else
        {
            throw new CliException($"{label} 不是核取方塊或切換按鈕");
        }

        return $"### Result\n已{(check ? "勾選" : "取消勾選")} {label}";
    }

    private string ExpandCollapse(CommandContext ctx, bool expand)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, expand ? $"展開{Friendly(el)}" : $"收合{Friendly(el)}");
        EnsureInteractable(ctx, el);
        var ok = expand ? el.TryExpand() : el.TryCollapse();
        if (!ok) throw new CliException($"{label} 不支援 ExpandCollapsePattern");
        Input.WaitUntilIdle();
        return $"### Result\n已{(expand ? "展開" : "收合")} {label}";
    }

    private string Invoke(CommandContext ctx)
    {
        var el = ResolveArg(ctx);
        var label = Label(el);
        BeforeAction(ctx, el, $"執行{Friendly(el)}");
        EnsureInteractable(ctx, el);
        if (!el.TryInvoke()) throw new CliException($"{label} 不支援 InvokePattern");
        Input.WaitUntilIdle();
        return $"### Result\n已觸發 {label}";
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
            var d => throw new CliException($"未知的捲動方向：{d}（可用：up, down, left, right）"),
        };
        var amount = Math.Max(1, ctx.Call.GetInt("amount") ?? 3);
        BeforeAction(ctx, el, $"捲動{Friendly(el)}");
        TryForeground(_s.RequireWindow());

        // 優先用 ScrollPattern；不支援時改用滑鼠滾輪
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
        return $"### Result\n已向 {direction.ToString().ToLowerInvariant()} 捲動 {Label(el)} {amount} 次";
    }

    private string Drag(CommandContext ctx)
    {
        var source = ResolveArg(ctx, "source");
        var dest = ResolveArg(ctx, "dest");
        BeforeAction(ctx, source, $"將{Friendly(source)}拖曳到{Friendly(dest)}");
        EnsureInteractable(ctx, source);
        Input.Drag(source.ClickPoint(), dest.ClickPoint());
        Input.WaitUntilIdle();
        return $"### Result\n已將 {Label(source)} 拖曳到 {Label(dest)}";
    }
}
