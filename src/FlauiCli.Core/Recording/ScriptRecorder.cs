using FlauiCli.Core.Engine;
using FlauiCli.Core.Protocol;
using FlauiCli.Core.Scripting;

namespace FlauiCli.Core.Recording;

/// <summary>收集錄製到的步驟（CLI 指令與真人操作共用），可匯出成 YAML 腳本。執行緒安全。</summary>
public sealed class ScriptRecorder(string? name, AppInfo? app)
{
    private readonly object _lock = new();
    private readonly List<CommandCall> _steps = [];

    public string? Name { get; } = name;

    public AppInfo? App { get; set; } = app;

    public DateTime StartedAt { get; } = DateTime.Now;

    public int Count
    {
        get { lock (_lock) return _steps.Count; }
    }

    public void Add(CommandCall call)
    {
        var c = call.Clone();
        c.Cwd = null;
        lock (_lock) _steps.Add(c);
    }

    public CommandCall? Last
    {
        get { lock (_lock) return _steps.Count > 0 ? _steps[^1] : null; }
    }

    public void ReplaceLast(CommandCall call)
    {
        lock (_lock)
        {
            if (_steps.Count > 0) _steps[^1] = call;
            else _steps.Add(call);
        }
    }

    public ScriptDocument ToDocument()
    {
        lock (_lock)
        {
            return new ScriptDocument
            {
                Name = Name ?? $"錄製於 {StartedAt:yyyy-MM-dd HH:mm}",
                App = App is null ? null : new AppSpec
                {
                    Launch = App.Launch,
                    Args = App.Args,
                    Window = App.Window,
                    Attach = App.Attach,
                },
                Steps = [.. _steps.Select(s => s.Clone())],
            };
        }
    }
}
