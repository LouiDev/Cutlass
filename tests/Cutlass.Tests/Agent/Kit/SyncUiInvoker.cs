using Cutlass.Agent.Hosting;

namespace Cutlass.Tests.Agent.Kit;

/// <summary>A "UI thread" that runs delegates inline, one at a time under a lock (HTTP tests call it from several threads).</summary>
internal sealed class SyncUiInvoker : IAgentUiInvoker
{
    private readonly object _gate = new();

    public bool CheckAccess() => Monitor.IsEntered(_gate);

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(ct);
        }

        try
        {
            lock (_gate)
            {
                return Task.FromResult(action());
            }
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ex);
        }
    }

    public Task InvokeAsync(Action action, CancellationToken ct) => InvokeAsync(
        () =>
        {
            action();
            return true;
        },
        ct);

    public void Post(Action action)
    {
        lock (_gate)
        {
            action();
        }
    }
}
