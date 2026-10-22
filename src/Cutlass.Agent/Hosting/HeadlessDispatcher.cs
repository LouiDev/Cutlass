using System.Collections.Concurrent;

namespace Cutlass.Agent.Hosting;

/// <summary>
/// The "UI thread" of the headless host (AGENT_INTEGRATION 2.5): a dedicated <c>cutlass-session</c> thread with a
/// delegate queue and its own <see cref="SynchronizationContext"/>, so continuations and <c>JobScheduler</c> callbacks
/// that capture the context land on it. Delegates must be short; an exception in a posted delegate is reported through
/// <see cref="UnhandledException"/> and does not stop the thread.
/// </summary>
public sealed class HeadlessDispatcher : IAgentUiInvoker, IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly SessionContext _context;
    private int _disposed;

    /// <summary>Starts the session thread.</summary>
    public HeadlessDispatcher()
    {
        _context = new SessionContext(this);
        _thread = new Thread(Run) { Name = "cutlass-session", IsBackground = true };
        _thread.Start();
    }

    /// <summary>Raised on the session thread when a posted delegate threw.</summary>
    public event Action<Exception>? UnhandledException;

    /// <summary>The session thread's synchronization context.</summary>
    public SynchronizationContext Context => _context;

    /// <inheritdoc />
    public bool CheckAccess() => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (CheckAccess())
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

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!TryEnqueue(() =>
        {
            if (ct.IsCancellationRequested)
            {
                tcs.TrySetCanceled(ct);
                return;
            }

            try
            {
                tcs.TrySetResult(action());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            tcs.TrySetException(new ObjectDisposedException(nameof(HeadlessDispatcher)));
        }

        return tcs.Task;
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        return InvokeAsync(
            () =>
            {
                action();
                return true;
            },
            ct);
    }

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        TryEnqueue(action);
    }

    /// <summary>Stops accepting work, runs what is queued and ends the thread.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        if (!CheckAccess())
        {
            _thread.Join(TimeSpan.FromSeconds(10));
        }
    }

    private bool TryEnqueue(Action action)
    {
        try
        {
            return !_queue.IsAddingCompleted && _queue.TryAdd(action);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(_context);
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                UnhandledException?.Invoke(ex);
            }
        }
    }

    private sealed class SessionContext(HeadlessDispatcher owner) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => owner.TryEnqueue(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (owner.CheckAccess())
            {
                d(state);
                return;
            }

            owner.InvokeAsync(() => d(state), CancellationToken.None).GetAwaiter().GetResult();
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
