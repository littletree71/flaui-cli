using System.Text.RegularExpressions;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Engine;

// 讀取、等待、斷言
public sealed partial class CommandDispatcher
{
    private static readonly HashSet<ControlKind> ValueTextKinds =
        [ControlKind.Edit, ControlKind.Document, ControlKind.ComboBox, ControlKind.Spinner];

    /// <summary>元素的「文字」：輸入類控制項取 Value / TextPattern，其他取 Name。</summary>
    internal static string GetText(IUiElement el) =>
        ValueTextKinds.Contains(el.Kind) ? el.Value ?? el.TryGetDocumentText() ?? el.Name : el.Name;

    private static string DescribeState(IUiElement el)
    {
        var states = new List<string>
        {
            el.IsEnabled ? "enabled" : "disabled",
            el.IsOffscreen ? "hidden" : "visible",
        };
        if (el.HasFocus) states.Add("focused");
        switch (el.Toggle)
        {
            case ToggleValue.On: states.Add("checked"); break;
            case ToggleValue.Off: states.Add("unchecked"); break;
            case ToggleValue.Indeterminate: states.Add("mixed"); break;
        }
        if (el.Expand is ExpandValue.Expanded) states.Add("expanded");
        if (el.Expand is ExpandValue.Collapsed) states.Add("collapsed");
        if (el.IsSelected == true) states.Add("selected");
        if (el.IsReadOnly == true) states.Add("readonly");
        return string.Join(", ", states);
    }

    private string Get(CommandContext ctx)
    {
        var kind = ctx.Call.Require("kind").ToLowerInvariant();
        var el = ResolveArg(ctx);
        var value = kind switch
        {
            "text" => GetText(el),
            "value" => el.Value ?? throw new CliException($"{Label(el)} 不支援 ValuePattern"),
            "name" => el.Name,
            "state" => DescribeState(el),
            "rect" => $"{el.Bounds.X},{el.Bounds.Y},{el.Bounds.Width},{el.Bounds.Height}",
            "prop" => el.GetProperty(ctx.Call.Require("prop"))
                      ?? throw new CliException($"{Label(el)} 不支援或讀不到屬性 {ctx.Call.Get("prop")}"),
            _ => throw new CliException($"未知的 get 類型：{kind}（可用：text, value, name, state, rect, prop）"),
        };
        ctx.Data["value"] = value;
        return value;
    }

    private string WaitFor(CommandContext ctx)
    {
        var raw = ctx.Call.Require("target");
        var state = (ctx.Call.Get("state") ?? "visible").ToLowerInvariant();
        Func<IUiElement?, bool> satisfied = state switch
        {
            "visible" => e => e is { IsOffscreen: false },
            "hidden" => e => e is null || e.IsOffscreen,
            "exists" or "attached" => e => e is not null,
            "gone" or "detached" => e => e is null,
            "enabled" => e => e is { IsEnabled: true },
            "disabled" => e => e is { IsEnabled: false },
            _ => throw new CliException($"未知的狀態：{state}（可用：visible, hidden, exists, gone, enabled, disabled）"),
        };

        var timeout = ctx.Timeout;
        if (!Poll(timeout, () => satisfied(_resolver.TryResolveOnce(raw))))
            throw new CliException($"等待逾時（{timeout}ms）：{raw} 未達到狀態 {state}");
        return $"### Result\n{raw} 已達到狀態 {state}";
    }

    private string WaitWindow(CommandContext ctx)
    {
        var title = ctx.Call.Require("title");
        var timeout = ctx.Timeout;
        IUiElement? found = null;
        if (!Poll(timeout, () => (found = FindTopLevelByTitle(title, _s.CurrentWindow?.ProcessId)) is not null))
            throw new CliException($"等待逾時（{timeout}ms）：找不到標題為「{title}」的視窗");

        if (!ctx.Call.GetBool("no-switch")) ActivateWindow(found!);
        return $"### Result\n視窗「{found!.Name}」已出現\n{WindowSection()}";
    }

    private static readonly string[] ValueAssertions = ["text", "contains", "matches", "value"];

    private static readonly (string Key, string Positive, string Negative)[] BoolAssertions =
    [
        ("exists", "exists", "not-exists"),
        ("visible", "visible", "hidden"),
        ("enabled", "enabled", "disabled"),
        ("checked", "checked", "unchecked"),
    ];

    private static readonly HashSet<string> KnownAssertions =
        ["exists", "not-exists", "visible", "hidden", "enabled", "disabled", "checked", "unchecked", "text", "contains", "matches", "value"];

    /// <summary>
    /// 解析斷言：CLI 形式 <c>assert text id=x "預期"</c>（kind + expected），
    /// 或 YAML 形式 <c>assert: { target: id=x, text: 預期 }</c>。
    /// </summary>
    internal static (string Kind, string? Expected) ResolveAssertion(CommandCall call)
    {
        string kind;
        string? expected;
        if (call.Get("kind") is { } k)
        {
            kind = k.ToLowerInvariant();
            expected = call.Get("expected");
        }
        else
        {
            var valueKey = ValueAssertions.FirstOrDefault(call.Has);
            if (valueKey is not null)
            {
                (kind, expected) = (valueKey, call.Get(valueKey));
            }
            else
            {
                var b = BoolAssertions.FirstOrDefault(b => call.Has(b.Key));
                if (b.Key is null)
                    throw new CliException("assert 需要指定條件，例如 text / contains / matches / value / exists / visible / enabled / checked");
                (kind, expected) = (call.GetBool(b.Key) ? b.Positive : b.Negative, null);
            }
        }

        if (!KnownAssertions.Contains(kind))
            throw new CliException($"未知的斷言類型：{kind}（可用：{string.Join(", ", KnownAssertions)}）");
        if (ValueAssertions.Contains(kind) && expected is null)
            throw new CliException($"assert {kind} 需要預期值");
        return (kind, expected);
    }

    /// <summary>評估斷言，通過回傳 null，失敗回傳原因。</summary>
    internal static string? EvaluateAssertion(string kind, string? expected, IUiElement? el, string target)
    {
        switch (kind)
        {
            case "exists": return el is null ? $"{target} 不存在" : null;
            case "not-exists": return el is not null ? $"{target} 仍然存在" : null;
            case "hidden": return el is null || el.IsOffscreen ? null : $"{target} 仍然可見";
        }

        if (el is null) return $"找不到元素 {target}";
        var exp = expected ?? "";
        return kind switch
        {
            "visible" => el.IsOffscreen ? $"{target} 不可見" : null,
            "enabled" => el.IsEnabled ? null : $"{target} 是停用狀態",
            "disabled" => el.IsEnabled ? $"{target} 是啟用狀態" : null,
            "checked" => IsChecked(el) ? null : $"{target} 未勾選",
            "unchecked" => IsChecked(el) ? $"{target} 已勾選" : null,
            "text" => GetText(el).Trim() == exp.Trim() ? null : $"{target} 的文字預期為「{exp}」，實際為「{GetText(el)}」",
            "contains" => GetText(el).Contains(exp, StringComparison.Ordinal) ? null : $"{target} 的文字應包含「{exp}」，實際為「{GetText(el)}」",
            "matches" => Regex.IsMatch(GetText(el), exp, RegexOptions.None, TimeSpan.FromSeconds(1)) ? null : $"{target} 的文字不符合 /{exp}/，實際為「{GetText(el)}」",
            "value" => el.Value == exp ? null : $"{target} 的值預期為「{exp}」，實際為「{el.Value ?? "（不支援 ValuePattern）"}」",
            _ => $"未知的斷言類型：{kind}",
        };
    }

    private static bool IsChecked(IUiElement el) => el.Toggle == ToggleValue.On || (el.Toggle is null && el.IsSelected == true);

    private string Assert(CommandContext ctx)
    {
        var raw = ctx.Call.Require("target");
        var (kind, expected) = ResolveAssertion(ctx.Call);
        string? failure = null;

        // 斷言會自動重試直到逾時（仿 Playwright expect）
        var passed = Poll(ctx.Timeout, () =>
        {
            failure = EvaluateAssertion(kind, expected, _resolver.TryResolveOnce(raw), raw);
            return failure is null;
        });
        if (!passed) throw new AssertionFailedException($"斷言失敗：{failure}");
        return $"### Result\n斷言通過：{raw} {kind}{(expected is null ? "" : $"「{expected}」")}";
    }

    private string Sleep(CommandContext ctx)
    {
        var ms = ctx.Call.GetInt("ms") ?? throw new CliException("sleep 需要毫秒數");
        Thread.Sleep(Math.Clamp(ms, 0, 600_000));
        return $"### Result\n已暫停 {ms}ms";
    }
}
