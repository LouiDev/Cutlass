using Avalonia.Threading;
using Cutlass.Agent.Hosting;

namespace Cutlass.App.Services.Agent;

/// <summary>
/// The App's <see cref="IAgentUiInvoker"/> (AGENT_INTEGRATION 2.3): <see cref="Dispatcher.UIThread"/> at Normal
/// priority. Every delegate is one synchronous unit (check the lock, run the edit, read the result), so no await can
/// sit between a lock check and the session call; only <see cref="Func{TResult}"/> and <see cref="Action"/> are
/// accepted, never an async delegate. On the UI thread the delegate runs inline.
/// </summary>
public sealed class AvaloniaUiInvoker : IAgentUiInvoker
{
    private readonly Dispatcher _dispatcher;

    /// <summary>Creates the invoker over the UI thread's dispatcher.</summary>
    public AvaloniaUiInvoker()
        : this(Dispatcher.UIThread)
    {
    }

    /// <summary>Creates the invoker over a dispatcher.</summary>
    /// <param name="dispatcher">The dispatcher.</param>
    public AvaloniaUiInvoker(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
    }

    /// <inheritdoc />
    public bool CheckAccess() => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(ct);
        }

        if (_dispatcher.CheckAccess())
        {
            try
            {
                return Task.FromResult(action());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, ct).GetTask();
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        if (_dispatcher.CheckAccess())
        {
            try
            {
                action();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        return _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, ct).GetTask();
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher.Post(action, DispatcherPriority.Normal);
    }
}
