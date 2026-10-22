using Cutlass.Agent.Hosting;

namespace Cutlass.Agent.Lock;

/// <summary>
/// Binds a host to its <see cref="AgentLock"/> and drives the lock's clock (AGENT_INTEGRATION 2.4 and 3.1). On
/// construction it routes <see cref="AgentLock.Changed"/> to <see cref="IAgentHost.OnLockChanged"/>, sets
/// <see cref="AgentLock.PrepareForAgent"/> and forwards <see cref="IAgentHost.UserStateChanged"/>. <see cref="Tick"/>
/// runs the 1 s tick on the UI thread; the App calls it from a <c>DispatcherTimer</c>, the headless host from
/// <see cref="Start"/>, which posts it through the invoker. Construct and dispose it on the UI thread.
/// </summary>
public sealed class AgentLockDriver : IDisposable
{
    private readonly IAgentHost _host;
    private readonly TimeProvider _time;
    private readonly AgentLock _lock;
    private ITimer? _timer;
    private bool _disposed;

    /// <summary>Binds the host to its lock. UI thread only.</summary>
    /// <param name="host">The host.</param>
    /// <param name="time">The clock, or null for the system clock.</param>
    public AgentLockDriver(IAgentHost host, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        _time = time ?? TimeProvider.System;
        _lock = host.Lock;
        _lock.Policy = host.Policy;
        _lock.PrepareForAgent = host.PrepareForAgent;
        _lock.Changed += OnLockChanged;
        host.UserStateChanged += OnUserStateChanged;
    }

    /// <summary>The current time in Unix milliseconds.</summary>
    public long NowMs => _time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Runs one tick on the UI thread: refreshes the policy and lets the lock expire leases and grant waiters.</summary>
    public void Tick()
    {
        if (_disposed)
        {
            return;
        }

        _lock.Policy = _host.Policy;
        _lock.Tick(NowMs, _host.UserState);
    }

    /// <summary>Starts posting <see cref="Tick"/> through the host's invoker every <paramref name="interval"/> (1 s by default).</summary>
    /// <param name="interval">The interval, or null for 1 s.</param>
    public void Start(TimeSpan? interval = null)
    {
        var period = interval ?? TimeSpan.FromSeconds(1);
        _timer?.Dispose();
        _timer = _time.CreateTimer(_ => _host.Ui.Post(Tick), null, period, period);
    }

    /// <summary>Stops the timer and unbinds the host. UI thread only.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer?.Dispose();
        _lock.Changed -= OnLockChanged;
        _host.UserStateChanged -= OnUserStateChanged;
    }

    private void OnLockChanged(object? sender, AgentLockState state) => _host.OnLockChanged(state);

    private void OnUserStateChanged(object? sender, EventArgs e)
    {
        if (!_disposed)
        {
            _lock.OnUserStateChanged(_host.UserState, NowMs);
        }
    }
}
