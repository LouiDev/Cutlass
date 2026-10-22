using Cutlass.Core.Session;

namespace Cutlass.Agent.Tools;

/// <summary>
/// The per-process revision counter (AGENT_INTEGRATION 4.2): raised on every <see cref="ProjectSession.Changed"/>
/// except <see cref="ChangeKind.Saved"/>. Readable from any thread.
/// </summary>
public sealed class AgentRevision : IDisposable
{
    private readonly ProjectSession _session;
    private long _value;

    /// <summary>Subscribes to the session. UI thread only.</summary>
    /// <param name="session">The session.</param>
    public AgentRevision(ProjectSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        session.Changed += OnChanged;
    }

    /// <summary>Raised on the UI thread after the revision moved, with the change.</summary>
    public event EventHandler<ProjectChangedEventArgs>? Raised;

    /// <summary>The current revision.</summary>
    public long Current => Interlocked.Read(ref _value);

    /// <summary>Unsubscribes. UI thread only.</summary>
    public void Dispose() => _session.Changed -= OnChanged;

    private void OnChanged(object? sender, ProjectChangedEventArgs e)
    {
        if (e.Kind == ChangeKind.Saved)
        {
            return;
        }

        Interlocked.Increment(ref _value);
        Raised?.Invoke(this, e);
    }
}
