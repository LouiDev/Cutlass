using Cutlass.Agent.Hosting;
using Cutlass.Agent.Lock;
using Cutlass.Core.Editing;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Agent;

/// <summary>Every transition of <see cref="AgentLock"/> with a manual clock (AGENT_INTEGRATION 3 and 7.1).</summary>
public sealed class AgentLockTests
{
    private static readonly AgentUserState Idle = AgentUserState.Idle;
    private readonly ProjectSession _session = new(new ProjectBuilder().With(V1, V("c1", 0, 50)).Build(), () => 0);
    private readonly List<AgentLockState> _changes = [];
    private long _now = 1_000_000;

    private AgentLock NewLock(AgentPolicy? policy = null)
    {
        var l = new AgentLock(_session) { Policy = policy ?? AgentPolicy.Default };
        l.Changed += (_, s) => _changes.Add(s);
        return l;
    }

    private static AcquireRequest Req(string name = "Claude Code", int waitMs = 0, int? lease = null, string? current = null) =>
        new(name, "Remove silences", waitMs, lease, current);

    [Fact]
    public void GrantWhenFreeRecordsTheLeaseAndClosesTheGate()
    {
        var l = NewLock();
        var prepared = 0;
        l.PrepareForAgent = () => prepared++;
        _session.Execute(p => TimelineEdits.AddMarker(new SequentialIdGenerator(), p, 5));
        var o = l.Acquire(Req(), Idle, _now);
        Assert.Equal(AcquireStatus.Granted, o.Status);
        Assert.NotNull(o.LockId);
        Assert.True(o.LockId!.Length >= 20);
        Assert.Equal(LockPhase.Held, l.State.Phase);
        Assert.Equal("Claude Code", l.State.AgentName);
        Assert.Equal(90, l.State.LeaseSeconds);
        Assert.Equal(_now + 90_000, l.State.LeaseExpiresAtMs);
        Assert.Equal(1, prepared);
        Assert.Equal(1, l.UndoBaseline);
        Assert.Equal("agent:Claude Code", l.State.Holder);

        // The gate refuses the user and lets the agent through.
        var before = _session.Current;
        Assert.Same(before, _session.Execute(p => TimelineEdits.Lift(p, ["c1"])).Project);
        using (_session.WriteAs(SessionWriter.Agent))
        {
            Assert.NotSame(before, _session.Execute(p => TimelineEdits.Lift(p, ["c1"])).Project);
        }
    }

    [Theory]
    [InlineData(true, false, false, "gesture")]
    [InlineData(false, true, false, "modal")]
    [InlineData(false, false, true, "export")]
    public void ABusyUserGivesBusyWithTheReason(bool gesture, bool modal, bool export, string reason)
    {
        var l = NewLock();
        var o = l.Acquire(Req(), new AgentUserState(gesture, modal, export, false), _now);
        Assert.Equal(AcquireStatus.Busy, o.Status);
        Assert.Equal(reason, o.BusyReason);
        Assert.Equal(LockPhase.Free, l.State.Phase);
    }

    [Fact]
    public void PlaybackIsNotBusy()
    {
        var l = NewLock();
        Assert.Equal(AcquireStatus.Granted, l.Acquire(Req(), new AgentUserState(false, false, false, true), _now).Status);
    }

    [Fact]
    public async Task PendingThenGrantedWhenTheUserBecomesIdle()
    {
        var l = NewLock();
        var o = l.Acquire(Req(waitMs: 10_000), new AgentUserState(true, false, false, false), _now);
        Assert.Equal(AcquireStatus.Waiting, o.Status);
        Assert.Equal(LockPhase.Pending, l.State.Phase);
        Assert.Equal("Claude Code", l.State.PendingAgent);
        l.OnUserStateChanged(new AgentUserState(true, false, false, false), _now + 100);
        Assert.False(o.Wait!.IsCompleted);
        l.OnUserStateChanged(Idle, _now + 200);
        var granted = await o.Wait;
        Assert.Equal(AcquireStatus.Granted, granted.Status);
        Assert.Equal(LockPhase.Held, l.State.Phase);
        Assert.True(l.IsHolder(granted.LockId));
    }

    [Fact]
    public async Task PendingTimesOutAsBusy()
    {
        var l = NewLock();
        var o = l.Acquire(Req(waitMs: 5_000), new AgentUserState(false, true, false, false), _now);
        l.Tick(_now + 4_999, new AgentUserState(false, true, false, false));
        Assert.False(o.Wait!.IsCompleted);
        l.Tick(_now + 5_000, new AgentUserState(false, true, false, false));
        var final = await o.Wait;
        Assert.Equal(AcquireStatus.Busy, final.Status);
        Assert.Equal("modal", final.BusyReason);
        Assert.Equal(LockPhase.Free, l.State.Phase);
    }

    [Fact]
    public async Task WaitersAreServedFirstInFirstOutAndAtMostFour()
    {
        var l = NewLock();
        var holder = l.Acquire(Req("A"), Idle, _now);
        var b = l.Acquire(Req("B", 60_000), Idle, _now);
        var c = l.Acquire(Req("C", 60_000), Idle, _now);
        var d = l.Acquire(Req("D", 60_000), Idle, _now);
        var e = l.Acquire(Req("E", 60_000), Idle, _now);
        var f = l.Acquire(Req("F", 60_000), Idle, _now);
        Assert.Equal(AcquireStatus.Waiting, b.Status);
        Assert.Equal(AcquireStatus.Waiting, e.Status);
        Assert.Equal(AcquireStatus.Busy, f.Status);
        Assert.Equal("held_by_agent", f.BusyReason);
        Assert.Equal("B", l.State.PendingAgent);
        Assert.True(l.Release(holder.LockId, Idle, _now + 10));
        Assert.Equal(AcquireStatus.Granted, (await b.Wait!).Status);
        Assert.Equal("B", l.State.AgentName);
        Assert.False(c.Wait!.IsCompleted);
        Assert.Equal("C", l.State.PendingAgent);
        l.Revoke(_now + 20);
        l.Tick(_now + 30, Idle);
        Assert.Equal(AcquireStatus.Granted, (await c.Wait).Status);
        Assert.False(d.Wait!.IsCompleted);
    }

    [Fact]
    public void ABusyAnswerWithoutWaitWhileAnotherAgentHolds()
    {
        var l = NewLock();
        l.Acquire(Req("A"), Idle, _now);
        var o = l.Acquire(Req("B"), Idle, _now);
        Assert.Equal(AcquireStatus.Busy, o.Status);
        Assert.Equal("held_by_agent", o.BusyReason);
    }

    [Fact]
    public void AcquireByTheHolderRenewsTheSameLock()
    {
        var l = NewLock();
        var id = l.Acquire(Req(), Idle, _now).LockId;
        var again = l.Acquire(Req(current: id), Idle, _now + 50_000);
        Assert.Equal(AcquireStatus.Granted, again.Status);
        Assert.True(again.Renewed);
        Assert.Equal(id, again.LockId);
        Assert.Equal(_now + 50_000 + 90_000, l.State.LeaseExpiresAtMs);
    }

    [Fact]
    public void TheLeaseIsClampedToFifteenAndTheMaximum()
    {
        var l = NewLock(AgentPolicy.Default with { MaxLeaseSeconds = 120 });
        l.Acquire(Req(lease: 5), Idle, _now);
        Assert.Equal(15, l.State.LeaseSeconds);
        l.Revoke(_now);
        l.Acquire(Req(lease: 600), Idle, _now);
        Assert.Equal(120, l.State.LeaseSeconds);
    }

    [Fact]
    public void AnyCallRenewsAndExpiryIsAtExactlyLeasePlusOneMillisecond()
    {
        var l = NewLock();
        var id = l.Acquire(Req(), Idle, _now).LockId;
        Assert.True(l.Touch(id, "status", _now + 60_000));
        Assert.Equal("status", l.State.LastTool);
        var expiry = _now + 60_000 + 90_000;
        Assert.Equal(expiry, l.State.LeaseExpiresAtMs);
        Assert.False(l.Touch("someone-else", "status", _now + 60_001));
        l.Tick(expiry, Idle);
        Assert.Equal(LockPhase.Held, l.State.Phase);
        l.Tick(expiry + 1, Idle);
        Assert.Equal(LockPhase.Free, l.State.Phase);
        Assert.Equal(LockLostReason.Expired, l.State.EndedReason);
        var check = l.Check(id, expiry + 2);
        Assert.Equal(LockCheckStatus.Lost, check.Status);
        Assert.Equal(LockLostReason.Expired, check.LostReason);
    }

    [Fact]
    public void ExpiryCommitsAnOpenBatch()
    {
        var l = NewLock();
        l.Acquire(Req(), Idle, _now);
        using (_session.WriteAs(SessionWriter.Agent))
        {
            _session.BeginGroup("Agent: Cut");
            _session.Execute(p => TimelineEdits.Lift(p, ["c1"]));
        }

        l.Tick(_now + 91_000, Idle);
        Assert.False(_session.InGroup);
        Assert.Equal("Agent: Cut", _session.UndoLabel);
    }

    [Fact]
    public async Task PauseFreezesTheLeaseAndResumeRestoresAtLeastThirtySeconds()
    {
        var l = NewLock();
        var id = l.Acquire(Req(), Idle, _now).LockId;
        Assert.True(l.Pause(_now + 80_000));
        Assert.Equal(LockPhase.Paused, l.State.Phase);
        Assert.Equal(10_000, l.State.PausedRemainingMs);
        Assert.Equal(LockCheckStatus.Paused, l.Check(id, _now + 80_001).Status);
        var resumed = l.WhenResumed;
        Assert.False(resumed.IsCompleted);

        // The lease does not run down while paused, and touches do not move it.
        l.Tick(_now + 500_000, Idle);
        Assert.Equal(LockPhase.Paused, l.State.Phase);
        Assert.True(l.Touch(id, "project.get", _now + 500_000));
        Assert.True(l.Resume(_now + 600_000));
        Assert.True(await resumed);
        Assert.Equal(_now + 600_000 + 30_000, l.State.LeaseExpiresAtMs);

        // With more left than 30 s, the remaining time comes back.
        Assert.True(l.Touch(id, "status", _now + 605_000));
        Assert.True(l.Pause(_now + 610_000));
        Assert.True(l.Resume(_now + 700_000));
        Assert.Equal(_now + 700_000 + 85_000, l.State.LeaseExpiresAtMs);
    }

    [Fact]
    public async Task RevokeWhilePausedFailsTheWaitingCalls()
    {
        var l = NewLock();
        var id = l.Acquire(Req(), Idle, _now).LockId;
        l.Pause(_now);
        var waiting = l.WhenResumed;
        Assert.True(l.Revoke(_now + 1));
        Assert.False(await waiting);
        Assert.Equal(LockPhase.Free, l.State.Phase);
        Assert.Equal(LockLostReason.RevokedByUser, l.Check(id, _now + 2).LostReason);
    }

    [Fact]
    public void EachLockLostReasonIsReported()
    {
        var l = NewLock();
        var released = l.Acquire(Req(), Idle, _now).LockId;
        Assert.True(l.Release(released, Idle, _now));
        var revoked = l.Acquire(Req(), Idle, _now).LockId;
        l.Revoke(_now);
        var expired = l.Acquire(Req(), Idle, _now).LockId;
        l.Tick(_now + 100_000, Idle);
        Assert.Equal(LockLostReason.Released, l.Check(released, _now + 100_001).LostReason);
        Assert.Equal(LockLostReason.RevokedByUser, l.Check(revoked, _now + 100_001).LostReason);
        Assert.Equal(LockLostReason.Expired, l.Check(expired, _now + 100_001).LostReason);
        Assert.Equal("expired", LockCheck.WireReason(LockLostReason.Expired));
        Assert.Equal("revoked_by_user", LockCheck.WireReason(LockLostReason.RevokedByUser));
    }

    [Fact]
    public void UnknownOrMissingIdsNeedTheLockAndNameTheHolder()
    {
        var l = NewLock();
        var none = l.Check(null, _now);
        Assert.Equal(LockCheckStatus.Required, none.Status);
        Assert.Equal("user", none.Holder);
        l.Acquire(Req("Cursor"), Idle, _now);
        var other = l.Check("nope", _now);
        Assert.Equal(LockCheckStatus.Required, other.Status);
        Assert.Equal("agent:Cursor", other.Holder);
    }

    [Fact]
    public void TheRevokedListForgetsAfterThirtyMinutes()
    {
        var l = NewLock();
        var id = l.Acquire(Req(), Idle, _now).LockId;
        l.Revoke(_now);
        Assert.Equal(LockCheckStatus.Lost, l.Check(id, _now + (30 * 60 * 1000)).Status);
        Assert.Equal(LockCheckStatus.Required, l.Check(id, _now + (30 * 60 * 1000) + 1).Status);
    }

    [Fact]
    public async Task AskFirstAllow()
    {
        var l = NewLock(AgentPolicy.Default with { AskFirst = true });
        var o = l.Acquire(Req(), Idle, _now);
        Assert.Equal(AcquireStatus.Waiting, o.Status);
        Assert.True(l.State.AskingUser);
        Assert.Equal(LockPhase.Pending, l.State.Phase);
        Assert.True(l.Answer(true, Idle, _now + 1000));
        var granted = await o.Wait!;
        Assert.Equal(AcquireStatus.Granted, granted.Status);
        Assert.False(l.State.AskingUser);
    }

    [Fact]
    public async Task AskFirstDeny()
    {
        var l = NewLock(AgentPolicy.Default with { AskFirst = true });
        var o = l.Acquire(Req(), Idle, _now);
        Assert.True(l.Answer(false, Idle, _now + 1000));
        var denied = await o.Wait!;
        Assert.Equal(AcquireStatus.Denied, denied.Status);
        Assert.Equal(LockPhase.Free, l.State.Phase);
    }

    [Fact]
    public async Task AskFirstWithoutAnAnswerIsDenied()
    {
        var l = NewLock(AgentPolicy.Default with { AskFirst = true });
        var o = l.Acquire(Req(), Idle, _now);
        l.Tick(_now + l.Options.AskTimeoutMs, Idle);
        Assert.Equal(AcquireStatus.Denied, (await o.Wait!).Status);
        Assert.False(l.State.AskingUser);
    }

    [Fact]
    public void AReplacedProjectResetsTheUndoBaseline()
    {
        var l = NewLock();
        _session.Execute(p => TimelineEdits.AddMarker(new SequentialIdGenerator(), p, 5));
        l.Acquire(Req(), Idle, _now);
        Assert.Equal(1, l.UndoBaseline);
        using (_session.WriteAs(SessionWriter.Agent))
        {
            _session.Replace(TimelineEdits.NewProject(new SequentialIdGenerator()));
        }

        Assert.Equal(0, l.UndoBaseline);
    }

    [Fact]
    public void ChangedFiresOnEveryTransition()
    {
        var l = NewLock();
        var id = l.Acquire(Req(), Idle, _now).LockId;
        l.Pause(_now);
        l.Resume(_now);
        l.Release(id, Idle, _now);
        Assert.Equal([LockPhase.Held, LockPhase.Paused, LockPhase.Held, LockPhase.Free], _changes.Select(c => c.Phase));
        Assert.Equal(LockLostReason.Released, _changes[^1].EndedReason);
        Assert.Equal("Claude Code", _changes[^1].EndedAgent);
    }
}
