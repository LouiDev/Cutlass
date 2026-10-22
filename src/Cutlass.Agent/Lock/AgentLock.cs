using System.Security.Cryptography;
using Cutlass.Agent.Hosting;
using Cutlass.Core.Session;

namespace Cutlass.Agent.Lock;

/// <summary>
/// The leased edit lock (AGENT_INTEGRATION 3): a state machine on the host's UI thread. Every method takes the time
/// as Unix milliseconds (<c>nowMs</c>), so tests drive it with a manual clock; <see cref="AgentLockDriver"/> calls
/// <see cref="Tick"/> each second. The constructor installs <see cref="ProjectSession.WriteGate"/>, which refuses
/// <see cref="SessionWriter.User"/> while an agent holds the project. Not thread-safe: UI thread only.
/// </summary>
public sealed class AgentLock
{
    private readonly ProjectSession _session;
    private readonly List<Waiter> _waiters = [];
    private readonly Dictionary<string, (LockLostReason Reason, long AtMs)> _revoked = new(StringComparer.Ordinal);
    private AgentLockState _state = AgentLockState.Free;
    private string? _lockId;
    private TaskCompletionSource<bool>? _resume;

    /// <summary>Creates the lock and installs the session's write gate.</summary>
    /// <param name="session">The session the lock guards.</param>
    /// <param name="options">The timing rules, or null for the defaults.</param>
    public AgentLock(ProjectSession session, LeaseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        Options = options ?? LeaseOptions.Default;
        session.WriteGate = writer => writer == SessionWriter.Agent || !_state.IsHeld;
        session.Changed += (_, e) =>
        {
            // A replaced project starts a new history: every step in it after the grant is the agent's.
            if (e.Kind == ChangeKind.Replace)
            {
                UndoBaseline = 0;
                AgentRedoCount = 0;
            }
        };
    }

    /// <summary>Raised on the UI thread after every transition (and on renewal, last tool and batch changes).</summary>
    public event EventHandler<AgentLockState>? Changed;

    /// <summary>The timing rules.</summary>
    public LeaseOptions Options { get; }

    /// <summary>The lease lengths and the "ask first" switch; hosts replace it when settings change.</summary>
    public AgentPolicy Policy { get; set; } = AgentPolicy.Default;

    /// <summary>Runs right before a grant: the host closes inline editors (<see cref="IAgentHost.PrepareForAgent"/>).</summary>
    public Action? PrepareForAgent { get; set; }

    /// <summary>The current state.</summary>
    public AgentLockState State => _state;

    /// <summary>True in Held and Paused.</summary>
    public bool IsHeld => _state.IsHeld;

    /// <summary>The session's undo count when the lock was granted: agent undo may not go below it.</summary>
    public int UndoBaseline { get; private set; }

    /// <summary>How many agent steps the agent undid and may redo; reset by a new agent edit.</summary>
    public int AgentRedoCount { get; set; }

    /// <summary>
    /// Completes with true on Resume and with false when the lock ends; a completed task while not paused. Mutating
    /// calls await it (off the UI thread) for at most <see cref="LeaseOptions.PausedWaitMs"/>.
    /// </summary>
    public Task<bool> WhenResumed => _resume?.Task ?? Task.FromResult(_state.IsHeld);

    /// <summary>True when <paramref name="lockId"/> is the current holder's id.</summary>
    /// <param name="lockId">The id.</param>
    /// <returns>True for the holder.</returns>
    public bool IsHolder(string? lockId) => lockId is not null && _lockId is not null && string.Equals(lockId, _lockId, StringComparison.Ordinal);

    /// <summary>
    /// Asks for the lock (AGENT_INTEGRATION 3.2): renews for its holder; queues (FIFO, at most 4) or fails busy while
    /// another agent holds it or the user is busy; asks the user first when the policy says so; else grants.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="user">What the user is doing.</param>
    /// <param name="nowMs">The time.</param>
    /// <returns>The outcome.</returns>
    public AcquireOutcome Acquire(AcquireRequest request, AgentUserState user, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);
        Forget(nowMs);
        var waitMs = Math.Clamp(request.WaitMs, 0, Options.MaxWaitMs);
        if (_state.IsHeld && IsHolder(request.CurrentLockId))
        {
            Renew(request.CurrentLockId, nowMs);
            return new AcquireOutcome(AcquireStatus.Granted, _lockId, _state, null, null) { Renewed = true };
        }

        if (_state.IsHeld || _waiters.Count > 0)
        {
            return Enqueue(request, waitMs, nowMs, "held_by_agent");
        }

        if (user.BusyReason is { } busy)
        {
            return Enqueue(request, waitMs, nowMs, busy);
        }

        if (Policy.AskFirst)
        {
            // The prompt needs an answer, so the request waits at least the prompt's own timeout.
            return Enqueue(request, Math.Max(waitMs, Options.AskTimeoutMs), nowMs, "asking");
        }

        var id = Grant(request, nowMs);
        return new AcquireOutcome(AcquireStatus.Granted, id, _state, null, null);
    }

    /// <summary>Renews the holder's lease (AGENT_INTEGRATION 3.3). Paused leases do not change.</summary>
    /// <param name="lockId">The caller's lock id.</param>
    /// <param name="nowMs">The time.</param>
    /// <returns>True when the caller holds the lock.</returns>
    public bool Renew(string? lockId, long nowMs)
    {
        if (!_state.IsHeld || !IsHolder(lockId))
        {
            return false;
        }

        if (_state.Phase == LockPhase.Held)
        {
            Set(_state with { LeaseExpiresAtMs = nowMs + (_state.LeaseSeconds * 1000L) });
        }

        return true;
    }

    /// <summary>
    /// Renews the holder's lease and records the tool for the banner. Calls by anyone else change nothing. Every call
    /// that carries the holder's lock id goes through here, reads included.
    /// </summary>
    /// <param name="lockId">The caller's lock id, or null.</param>
    /// <param name="tool">The tool name.</param>
    /// <param name="nowMs">The time.</param>
    /// <returns>True when the caller holds the lock.</returns>
    public bool Touch(string? lockId, string tool, long nowMs)
    {
        if (!_state.IsHeld || !IsHolder(lockId))
        {
            return false;
        }

        var lease = _state.Phase == LockPhase.Held ? nowMs + (_state.LeaseSeconds * 1000L) : _state.LeaseExpiresAtMs;
        Set(_state with { LeaseExpiresAtMs = lease, LastTool = tool, LastToolAtMs = nowMs });
        return true;
    }

    /// <summary>Checks a mutating call's lock id (AGENT_INTEGRATION 3.5).</summary>
    /// <param name="lockId">The caller's lock id, or null.</param>
    /// <param name="nowMs">The time.</param>
    /// <returns>Ok, paused, required (with the holder) or lost (with the reason).</returns>
    public LockCheck Check(string? lockId, long nowMs)
    {
        Forget(nowMs);
        var holder = _state.Holder;
        if (_state.IsHeld && IsHolder(lockId))
        {
            return new LockCheck(_state.Phase == LockPhase.Paused ? LockCheckStatus.Paused : LockCheckStatus.Ok, holder, null);
        }

        if (lockId is not null && _revoked.TryGetValue(lockId, out var revoked))
        {
            return new LockCheck(LockCheckStatus.Lost, holder, revoked.Reason);
        }

        return new LockCheck(LockCheckStatus.Required, holder, null);
    }

    /// <summary>Releases the lock (AGENT_INTEGRATION 3.5): commits an open batch and frees the project.</summary>
    /// <param name="lockId">The caller's lock id.</param>
    /// <param name="user">What the user is doing (for the next waiter).</param>
    /// <param name="nowMs">The time.</param>
    /// <returns>True when the caller held the lock.</returns>
    public bool Release(string? lockId, AgentUserState user, long nowMs)
    {
        if (!_state.IsHeld || !IsHolder(lockId))
        {
            return false;
        }

        End(LockLostReason.Released, nowMs);
        GrantNext(user, nowMs);
        return true;
    }

    /// <summary>Take over (AGENT_INTEGRATION 3.5): commits an open batch and frees the project at once.</summary>
    /// <param name="nowMs">The time.</param>
    /// <returns>True when an agent held the lock.</returns>
    public bool Revoke(long nowMs)
    {
        if (!_state.IsHeld)
        {
            return false;
        }

        End(LockLostReason.RevokedByUser, nowMs);
        return true;
    }

    /// <summary>Pause (AGENT_INTEGRATION 3.4): stops the lease clock; mutating calls wait.</summary>
    /// <param name="nowMs">The time.</param>
    /// <returns>True when the lock was held and is now paused.</returns>
    public bool Pause(long nowMs)
    {
        if (_state.Phase != LockPhase.Held)
        {
            return false;
        }

        var remaining = Math.Max(0, (_state.LeaseExpiresAtMs ?? nowMs) - nowMs);
        _resume = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(_state with { Phase = LockPhase.Paused, PausedRemainingMs = remaining });
        return true;
    }

    /// <summary>Resume: restores the remaining lease, at least 30 s, and releases the waiting calls.</summary>
    /// <param name="nowMs">The time.</param>
    /// <returns>True when the lock was paused.</returns>
    public bool Resume(long nowMs)
    {
        if (_state.Phase != LockPhase.Paused)
        {
            return false;
        }

        var remaining = Math.Max(_state.PausedRemainingMs ?? 0, Options.ResumeMinimumMs);
        Set(_state with { Phase = LockPhase.Held, PausedRemainingMs = null, LeaseExpiresAtMs = nowMs + remaining });
        var resume = _resume;
        _resume = null;
        resume?.TrySetResult(true);
        return true;
    }

    /// <summary>The user's answer to the "ask first" prompt for the first waiting agent.</summary>
    /// <param name="allow">True for Allow, false for Deny.</param>
    /// <param name="user">What the user is doing.</param>
    /// <param name="nowMs">The time.</param>
    /// <returns>True when a prompt was waiting.</returns>
    public bool Answer(bool allow, AgentUserState user, long nowMs)
    {
        if (!_state.AskingUser || _waiters.Count == 0)
        {
            return false;
        }

        var waiter = _waiters[0];
        if (!allow)
        {
            _waiters.RemoveAt(0);
            waiter.Completion.TrySetResult(new AcquireOutcome(AcquireStatus.Denied, null, _state, "denied_by_user", null));
            RefreshPending();
            GrantNext(user, nowMs);
            return true;
        }

        waiter.Approved = true;
        GrantNext(user, nowMs);
        return true;
    }

    /// <summary>
    /// The 1 s tick: expires a lease that is over (committing an open batch), times out waiters, forgets old revoked
    /// ids and grants to the next waiter when possible.
    /// </summary>
    /// <param name="nowMs">The time.</param>
    /// <param name="user">What the user is doing.</param>
    public void Tick(long nowMs, AgentUserState user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (_state.Phase == LockPhase.Held && _state.LeaseExpiresAtMs is { } expiry && nowMs > expiry)
        {
            End(LockLostReason.Expired, nowMs);
        }

        for (var i = _waiters.Count - 1; i >= 0; i--)
        {
            var waiter = _waiters[i];
            if (nowMs >= waiter.DeadlineMs)
            {
                _waiters.RemoveAt(i);
                var asked = i == 0 && _state.AskingUser;
                waiter.Completion.TrySetResult(asked
                    ? new AcquireOutcome(AcquireStatus.Denied, null, _state, "denied_by_user", null)
                    : new AcquireOutcome(AcquireStatus.Busy, null, _state, waiter.BusyReason(_state, user), null));
            }
        }

        Forget(nowMs);
        RefreshPending();
        GrantNext(user, nowMs);
    }

    /// <summary>Called when the user state changed (a gesture ended, a dialog closed): grants to a waiter when the user is idle.</summary>
    /// <param name="user">What the user is doing.</param>
    /// <param name="nowMs">The time.</param>
    public void OnUserStateChanged(AgentUserState user, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(user);
        GrantNext(user, nowMs);
    }

    /// <summary>Gives up a waiting acquisition (its client stopped waiting).</summary>
    /// <param name="wait">The <see cref="AcquireOutcome.Wait"/> task.</param>
    public void CancelWait(Task<AcquireOutcome> wait)
    {
        var index = _waiters.FindIndex(w => w.Completion.Task == wait);
        if (index < 0)
        {
            return;
        }

        var waiter = _waiters[index];
        _waiters.RemoveAt(index);
        waiter.Completion.TrySetResult(new AcquireOutcome(AcquireStatus.Busy, null, _state, "held_by_agent", null));
        RefreshPending();
    }

    /// <summary>Records the open batch label for the banner (null when the batch ends).</summary>
    /// <param name="label">The label without the "Agent: " prefix, or null.</param>
    public void NoteBatch(string? label)
    {
        if (_state.IsHeld && !string.Equals(_state.BatchLabel, label, StringComparison.Ordinal))
        {
            Set(_state with { BatchLabel = label });
        }
    }

    private AcquireOutcome Enqueue(AcquireRequest request, int waitMs, long nowMs, string busyReason)
    {
        if (waitMs <= 0 || _waiters.Count >= Options.MaxWaiters)
        {
            return new AcquireOutcome(AcquireStatus.Busy, null, _state, busyReason, null);
        }

        var waiter = new Waiter(request, nowMs + waitMs);
        _waiters.Add(waiter);
        RefreshPending();
        return new AcquireOutcome(AcquireStatus.Waiting, null, _state, busyReason, waiter.Completion.Task);
    }

    private void GrantNext(AgentUserState user, long nowMs)
    {
        if (_state.IsHeld || _waiters.Count == 0)
        {
            RefreshPending();
            return;
        }

        var waiter = _waiters[0];
        if (user.BusyReason is not null || (Policy.AskFirst && !waiter.Approved))
        {
            RefreshPending();
            return;
        }

        _waiters.RemoveAt(0);
        var id = Grant(waiter.Request, nowMs);
        waiter.Completion.TrySetResult(new AcquireOutcome(AcquireStatus.Granted, id, _state, null, null));
    }

    private string Grant(AcquireRequest request, long nowMs)
    {
        PrepareForAgent?.Invoke();
        var max = Math.Clamp(Policy.MaxLeaseSeconds, Options.MinLeaseSeconds, AgentPolicy.MaxLeaseLimitSeconds);
        var lease = Math.Clamp(request.LeaseSeconds ?? Policy.DefaultLeaseSeconds, Options.MinLeaseSeconds, max);
        _lockId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        UndoBaseline = _session.UndoCount;
        AgentRedoCount = 0;
        var first = _waiters.Count > 0 ? _waiters[0].Request : null;
        Set(new AgentLockState(
            LockPhase.Held,
            request.AgentName,
            request.Reason,
            nowMs,
            nowMs + (lease * 1000L),
            lease,
            null,
            null,
            null,
            first?.AgentName,
            first?.Reason,
            false,
            null,
            null,
            null));
        return _lockId;
    }

    private void End(LockLostReason reason, long nowMs)
    {
        if (_session.InGroup)
        {
            using (_session.WriteAs(SessionWriter.Agent))
            {
                _session.EndGroup();
            }
        }

        var agent = _state.AgentName;
        if (_lockId is not null)
        {
            _revoked[_lockId] = (reason, nowMs);
        }

        _lockId = null;
        var resume = _resume;
        _resume = null;
        var first = _waiters.Count > 0 ? _waiters[0].Request : null;
        Set(AgentLockState.Free with
        {
            Phase = first is null ? LockPhase.Free : LockPhase.Pending,
            PendingAgent = first?.AgentName,
            PendingReason = first?.Reason,
            EndedReason = reason,
            EndedAgent = agent,
        });
        resume?.TrySetResult(false);
    }

    private void RefreshPending()
    {
        var first = _waiters.Count > 0 ? _waiters[0] : null;
        if (_state.IsHeld)
        {
            if (!string.Equals(_state.PendingAgent, first?.Request.AgentName, StringComparison.Ordinal))
            {
                Set(_state with { PendingAgent = first?.Request.AgentName, PendingReason = first?.Request.Reason });
            }

            return;
        }

        var phase = first is null ? LockPhase.Free : LockPhase.Pending;
        var asking = first is not null && Policy.AskFirst && !first.Approved;
        if (_state.Phase != phase || _state.AskingUser != asking
            || !string.Equals(_state.PendingAgent, first?.Request.AgentName, StringComparison.Ordinal))
        {
            Set(_state with
            {
                Phase = phase,
                PendingAgent = first?.Request.AgentName,
                PendingReason = first?.Request.Reason,
                AskingUser = asking,
            });
        }
    }

    private void Forget(long nowMs)
    {
        if (_revoked.Count == 0)
        {
            return;
        }

        foreach (var key in _revoked.Where(r => nowMs - r.Value.AtMs > Options.RevokedMemoryMs).Select(r => r.Key).ToList())
        {
            _revoked.Remove(key);
        }
    }

    private void Set(AgentLockState state)
    {
        _state = state;
        Changed?.Invoke(this, state);
    }

    private sealed class Waiter(AcquireRequest request, long deadlineMs)
    {
        public AcquireRequest Request { get; } = request;

        public long DeadlineMs { get; } = deadlineMs;

        public bool Approved { get; set; }

        public TaskCompletionSource<AcquireOutcome> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string BusyReason(AgentLockState state, AgentUserState user) =>
            state.IsHeld ? "held_by_agent" : user.BusyReason ?? "held_by_agent";
    }
}
