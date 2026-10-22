namespace Cutlass.Agent.Hosting;

/// <summary>
/// Runs delegates on the host's UI thread (AGENT_INTEGRATION 2.3): the App wraps <c>Dispatcher.UIThread</c> at Normal
/// priority, the headless host its session thread. Delegates must be short and synchronous.
/// </summary>
public interface IAgentUiInvoker
{
    /// <summary>True when the caller already runs on the UI thread.</summary>
    /// <returns>True on the UI thread.</returns>
    bool CheckAccess();

    /// <summary>Runs a function on the UI thread (inline when already there) and returns its result.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="action">The function.</param>
    /// <param name="ct">Cancels the call while it waits to run.</param>
    /// <returns>The result; exceptions of the function propagate.</returns>
    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken ct);

    /// <summary>Runs an action on the UI thread (inline when already there).</summary>
    /// <param name="action">The action.</param>
    /// <param name="ct">Cancels the call while it waits to run.</param>
    /// <returns>A task that completes after the action; exceptions propagate.</returns>
    Task InvokeAsync(Action action, CancellationToken ct);

    /// <summary>Queues an action on the UI thread without waiting.</summary>
    /// <param name="action">The action.</param>
    void Post(Action action);
}
