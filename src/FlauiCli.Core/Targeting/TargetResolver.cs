using System.Diagnostics;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Engine;

namespace FlauiCli.Core.Targeting;

/// <summary>Resolves a ref or selector to an element; selectors auto-wait until the timeout (like Playwright's auto-wait).</summary>
internal sealed class TargetResolver(AutomationSession session)
{
    internal int PollIntervalMs { get; init; } = 150;

    public IUiElement Resolve(string raw, int timeoutMs)
    {
        var selector = Selector.Parse(raw);
        if (selector.IsRef) return ResolveRef(selector.Ref!);

        var sw = Stopwatch.StartNew();
        while (true)
        {
            var all = ElementFinder.FindAll(selector, session.SearchRoots());
            if (all.Count > 0) return all[0];
            if (sw.ElapsedMilliseconds >= timeoutMs)
                throw new ElementNotFoundException(raw, $"waited {timeoutMs}ms");
            Thread.Sleep(PollIntervalMs);
        }
    }

    /// <summary>Tries once and returns null when nothing is found (used by wait / assert).</summary>
    public IUiElement? TryResolveOnce(string raw)
    {
        var selector = Selector.Parse(raw);
        if (selector.IsRef)
        {
            try { return ResolveRef(selector.Ref!); }
            catch (CliException) { return null; }
        }
        var all = ElementFinder.FindAll(selector, session.SearchRoots());
        return all.Count > 0 ? all[0] : null;
    }

    private IUiElement ResolveRef(string r)
    {
        if (!session.Refs.TryGet(r, out var entry))
            throw new CliException($"Unknown ref: {r}. Run snapshot to get current refs");

        if (entry.Element.IsAlive) return entry.Element;

        // The element went stale: look it up again using the recorded AutomationId / Name
        var candidates = new List<string>();
        var kind = entry.Kind.ToString();
        if (entry.AutomationId.Length > 0) candidates.Add($"id={Selector.Quote(entry.AutomationId)}&&type={kind}");
        if (entry.Name.Length > 0) candidates.Add($"name={Selector.Quote(entry.Name)}&&type={kind}");
        foreach (var c in candidates)
        {
            var found = ElementFinder.FindAll(Selector.Parse(c), session.SearchRoots());
            if (found.Count == 1)
            {
                session.Refs.Update(r, found[0]);
                return found[0];
            }
        }

        throw new CliException($"The element for ref {r} no longer exists. Run snapshot again");
    }
}
