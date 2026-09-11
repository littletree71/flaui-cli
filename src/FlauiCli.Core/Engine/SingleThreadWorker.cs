using System.Collections.Concurrent;

namespace FlauiCli.Core.Engine;

/// <summary>Runs work items sequentially on one dedicated thread (every UIA call goes through it to avoid COM apartment issues).</summary>
public sealed class SingleThreadWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;

    public SingleThreadWorker(string name)
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public T Invoke<T>(Func<T> func) => InvokeAsync(func).GetAwaiter().GetResult();

    public void Invoke(Action action) => Invoke(() => { action(); return 0; });

    private void Loop()
    {
        foreach (var work in _queue.GetConsumingEnumerable()) work();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(5));
        _queue.Dispose();
    }
}
