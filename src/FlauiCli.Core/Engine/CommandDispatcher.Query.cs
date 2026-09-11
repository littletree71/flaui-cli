using System.Text.RegularExpressions;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Protocol;

namespace FlauiCli.Core.Engine;

// Read, wait and assert commands
public sealed partial class CommandDispatcher
{
    private static readonly HashSet<ControlKind> ValueTextKinds =
        [ControlKind.Edit, ControlKind.Document, ControlKind.ComboBox, ControlKind.Spinner];

    /// <summary>The "text" of an element: Value / TextPattern for input controls, Name for everything else.</summary>
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
            "value" => el.Value ?? throw new CliException($"{Label(el)} does not support ValuePattern"),
            "name" => el.Name,
            "state" => DescribeState(el),
            "rect" => $"{el.Bounds.X},{el.Bounds.Y},{el.Bounds.Width},{el.Bounds.Height}",
            "prop" => el.GetProperty(ctx.Call.Require("prop"))
                      ?? throw new CliException($"{Label(el)} does not support property {ctx.Call.Get("prop")} or it cannot be read"),
            _ => throw new CliException($"Unknown get kind: {kind} (use text, value, name, state, rect or prop)"),
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
            _ => throw new CliException($"Unknown state: {state} (use visible, hidden, exists, gone, enabled or disabled)"),
        };

        var timeout = ctx.Timeout;
        if (!Poll(timeout, () => satisfied(_resolver.TryResolveOnce(raw))))
            throw new CliException($"Timed out after {timeout}ms: {raw} did not reach state '{state}'");
        return $"### Result\n{raw} reached state '{state}'";
    }

    private string WaitWindow(CommandContext ctx)
    {
        var title = ctx.Call.Require("title");
        var timeout = ctx.Timeout;
        IUiElement? found = null;
        if (!Poll(timeout, () => (found = FindTopLevelByTitle(title, _s.CurrentWindow?.ProcessId)) is not null))
            throw new CliException($"Timed out after {timeout}ms: no window titled \"{title}\"");

        if (!ctx.Call.GetBool("no-switch")) ActivateWindow(found!);
        return $"### Result\nWindow \"{found!.Name}\" appeared\n{WindowSection()}";
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
    /// Parses an assertion. CLI form: <c>assert text id=x "expected"</c> (kind + expected);
    /// YAML form: <c>assert: { target: id=x, text: expected }</c>.
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
                    throw new CliException("assert needs a condition, for example text / contains / matches / value / exists / visible / enabled / checked");
                (kind, expected) = (call.GetBool(b.Key) ? b.Positive : b.Negative, null);
            }
        }

        if (!KnownAssertions.Contains(kind))
            throw new CliException($"Unknown assertion kind: {kind} (use: {string.Join(", ", KnownAssertions)})");
        if (ValueAssertions.Contains(kind) && expected is null)
            throw new CliException($"assert {kind} needs an expected value");
        return (kind, expected);
    }

    /// <summary>Evaluates an assertion: returns null when it passes, otherwise the reason it failed.</summary>
    internal static string? EvaluateAssertion(string kind, string? expected, IUiElement? el, string target)
    {
        switch (kind)
        {
            case "exists": return el is null ? $"{target} does not exist" : null;
            case "not-exists": return el is not null ? $"{target} still exists" : null;
            case "hidden": return el is null || el.IsOffscreen ? null : $"{target} is still visible";
        }

        if (el is null) return $"Element not found: {target}";
        var exp = expected ?? "";
        return kind switch
        {
            "visible" => el.IsOffscreen ? $"{target} is not visible" : null,
            "enabled" => el.IsEnabled ? null : $"{target} is disabled",
            "disabled" => el.IsEnabled ? $"{target} is enabled" : null,
            "checked" => IsChecked(el) ? null : $"{target} is not checked",
            "unchecked" => IsChecked(el) ? $"{target} is checked" : null,
            "text" => GetText(el).Trim() == exp.Trim() ? null : $"expected the text of {target} to be \"{exp}\" but it was \"{GetText(el)}\"",
            "contains" => GetText(el).Contains(exp, StringComparison.Ordinal) ? null : $"expected the text of {target} to contain \"{exp}\" but it was \"{GetText(el)}\"",
            "matches" => Regex.IsMatch(GetText(el), exp, RegexOptions.None, TimeSpan.FromSeconds(1)) ? null : $"expected the text of {target} to match /{exp}/ but it was \"{GetText(el)}\"",
            "value" => el.Value == exp ? null : $"expected the value of {target} to be \"{exp}\" but it was \"{el.Value ?? "(no ValuePattern)"}\"",
            _ => $"Unknown assertion kind: {kind}",
        };
    }

    private static bool IsChecked(IUiElement el) => el.Toggle == ToggleValue.On || (el.Toggle is null && el.IsSelected == true);

    private string Assert(CommandContext ctx)
    {
        var raw = ctx.Call.Require("target");
        var (kind, expected) = ResolveAssertion(ctx.Call);
        string? failure = null;

        // Assertions retry until the timeout (like Playwright's expect)
        var passed = Poll(ctx.Timeout, () =>
        {
            failure = EvaluateAssertion(kind, expected, _resolver.TryResolveOnce(raw), raw);
            return failure is null;
        });
        if (!passed) throw new AssertionFailedException($"Assertion failed: {failure}");
        return $"### Result\nAssertion passed: {raw} {kind}{(expected is null ? "" : $" \"{expected}\"")}";
    }

    private string Sleep(CommandContext ctx)
    {
        var ms = ctx.Call.GetInt("ms") ?? throw new CliException("sleep needs a number of milliseconds");
        Thread.Sleep(Math.Clamp(ms, 0, 600_000));
        return $"### Result\nSlept {ms}ms";
    }
}
