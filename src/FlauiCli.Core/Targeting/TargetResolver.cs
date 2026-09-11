using System.Diagnostics;
using FlauiCli.Core.Abstractions;
using FlauiCli.Core.Engine;

namespace FlauiCli.Core.Targeting;

/// <summary>把 ref 或 selector 解析成元素；selector 會自動等待直到逾時（仿 Playwright auto-wait）。</summary>
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
                throw new ElementNotFoundException(raw, $"已等待 {timeoutMs}ms");
            Thread.Sleep(PollIntervalMs);
        }
    }

    /// <summary>只嘗試一次，找不到回傳 null（wait / assert 用）。</summary>
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
            throw new CliException($"未知的 ref：{r}。請先執行 snapshot 取得最新的 ref");

        if (entry.Element.IsAlive) return entry.Element;

        // 元素已失效：用記錄的 AutomationId / Name 重新尋找
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

        throw new CliException($"ref {r} 對應的元素已不存在。請重新執行 snapshot");
    }
}
