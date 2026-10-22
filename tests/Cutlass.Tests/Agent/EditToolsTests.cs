using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Lock;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Tests.Agent.Kit;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Agent;

/// <summary>Every edit tool through <c>ToolDispatcher</c> on a <see cref="FakeAgentHost"/> (AGENT_INTEGRATION 7.1).</summary>
public sealed class EditToolsTests : IDisposable
{
    private readonly FakeAgentHost _host;

    public EditToolsTests()
    {
        _host = new FakeAgentHost(Sample(), LeaseOptions.Default with { PausedWaitMs = 300 });
    }

    public void Dispose() => _host.Dispose();

    private static Project Sample() => new ProjectBuilder()
        .With(V1, V("v1", 0, 100, link: "l_1"), V("v2", 100, 100, sourceFrame: 500, link: "l_2"))
        .With(A1, A("a1", 0, 100, link: "l_1"), A("a2", 100, 100, sourceFrame: 500, link: "l_2"))
        .With(V2, Title("t1", 0, 50, "Hello"))
        .Transition(V1, "x_1", "v1", "v2", 10)
        .Marker("k_1", 30, "Drop")
        .Build();

    private Project Current => _host.Session.Current;

    private async Task<ApiResult> Ok(string tool, string json, string lockId)
    {
        var r = await _host.CallAsync(tool, json, lockId);
        Assert.True(r.Error is null, r.Error?.CodeName + ": " + r.Error?.Message);
        return r;
    }

    private async Task<ApiError> Fails(string tool, string json, string? lockId, ErrorCode code)
    {
        var r = await _host.CallAsync(tool, json, lockId);
        Assert.NotNull(r.Error);
        Assert.Equal(code, r.Error.Code);
        return r.Error;
    }

    [Fact]
    public async Task MutatingCallsWithoutALockNeedOne()
    {
        var error = await Fails("edit.split", """{"frame":50}""", null, ErrorCode.LockRequired);
        Assert.Equal("user", error.Detail("holder"));
        Assert.Contains("lock/acquire", error.Message, StringComparison.Ordinal);
        await Fails("edit.split", """{"frame":50}""", "made-up", ErrorCode.LockRequired);
        Assert.Equal(0, _host.Session.UndoCount);
    }

    [Fact]
    public async Task ReadsWorkWithoutALock()
    {
        Assert.Null((await _host.CallAsync("status")).Error);
        Assert.Null((await _host.CallAsync("project.get")).Error);
        var describe = await _host.CallAsync("project.describe");
        Assert.Contains("V1 tr_v1:", describe.Text, StringComparison.Ordinal);
        var media = await _host.CallAsync("media.list");
        Assert.Equal(TestMedia.All.Length, media.Result!.Value.GetArrayLength());
    }

    [Fact]
    public async Task SplitIsLabelledAsAnAgentStepAndReturnsTheEnvelope()
    {
        var id = await _host.AcquireAsync();
        var r = await Ok("edit.split", """{"frame":50,"returnProject":true}""", id);
        Assert.True(r.Changed);
        Assert.Equal("Agent: Split", r.UndoLabel);
        Assert.Equal("Split", r.Result!.Value.GetProperty("label").GetString());
        Assert.NotNull(r.Select);
        Assert.NotNull(r.Project);
        Assert.Equal(3, ModelAssert.Track(Current, V1).Clips.Length);
        Assert.Single(_host.Edits);
        Assert.True(r.Revision > 0);
        ModelAssert.Valid(Current);
    }

    [Fact]
    public async Task UnknownIdsAreNotFoundAndNothingChanges()
    {
        var id = await _host.AcquireAsync();
        var before = Current;
        Assert.Equal("clip", (await Fails("edit.move", """{"clipIds":["nope"],"deltaFrames":5}""", id, ErrorCode.NotFound)).Detail("kind"));
        await Fails("edit.trim", """{"clipId":"nope","edge":"end","deltaFrames":5}""", id, ErrorCode.NotFound);
        await Fails("edit.split", """{"frame":5,"trackIds":["tr_nope"]}""", id, ErrorCode.NotFound);
        await Fails("edit.transition.update", """{"transitionId":"x_nope","durationFrames":4}""", id, ErrorCode.NotFound);
        await Fails("edit.marker.remove", """{"markerIds":["k_nope"]}""", id, ErrorCode.NotFound);
        await Fails("edit.track.update", """{"trackId":"tr_nope","locked":true}""", id, ErrorCode.NotFound);
        await Fails("edit.place", """{"mediaId":"m_nope","at":0}""", id, ErrorCode.NotFound);
        Assert.Same(before, Current);
    }

    [Fact]
    public async Task BadArgumentsAreInvalidWithTheField()
    {
        var id = await _host.AcquireAsync();
        Assert.Equal("frame", (await Fails("edit.split", "{}", id, ErrorCode.InvalidArgument)).Detail("field"));
        Assert.Equal("frame", (await Fails("edit.split", """{"frame":"soon"}""", id, ErrorCode.InvalidArgument)).Detail("field"));
        Assert.Equal("mode", (await Fails("edit.move", """{"clipIds":["v1"],"deltaFrames":1,"mode":"sideways"}""", id, ErrorCode.InvalidArgument)).Detail("field"));
        Assert.Equal("percent", (await Fails("edit.speed", """{"clipIds":["v1"],"percent":1000}""", id, ErrorCode.InvalidArgument)).Detail("field"));
        await Fails("edit.trim", """{"clipId":"v1","edge":"end","deltaFrames":1,"toFrame":3}""", id, ErrorCode.InvalidArgument);
        await Fails("edit.place", """{"mediaId":"m_long","at":0,"sourceInFlicks":1,"sourceInSeconds":1}""", id, ErrorCode.InvalidArgument);
        await Fails("edit.marker.add", """{"frame":3,"color":"red"}""", id, ErrorCode.InvalidArgument);
        await Fails("edit.remove-ranges", """{"ranges":[{"start":10,"end":5}]}""", id, ErrorCode.InvalidArgument);
        await Fails("edit.volume-keys", """{"clipId":"v1","keys":[]}""", id, ErrorCode.InvalidArgument);
        await Fails("edit.title.add", """{"trackId":"tr_a1","at":0,"durationFrames":5,"title":{"text":"x"}}""", id, ErrorCode.InvalidArgument);
        await Fails("nope.tool", "{}", id, ErrorCode.NotFound);
    }

    [Fact]
    public async Task EveryClipEditWorks()
    {
        var id = await _host.AcquireAsync();
        await Ok("edit.place", """{"mediaId":"m_short","at":200,"sourceInSeconds":1,"sourceOutSeconds":2}""", id);
        var placed = ModelAssert.Track(Current, V1).Clips[^1];
        Assert.Equal(200, placed.Start);
        Assert.Equal(25, placed.Duration);
        Assert.Equal(25 * F, ((MediaClip)placed).SourceInFlicks);
        Assert.Equal(25, ModelAssert.Track(Current, A1).Clips[^1].Duration);

        await Ok("edit.place", """{"mediaId":"m_still","at":300,"streams":"video","durationFrames":10}""", id);
        Assert.Equal(10, ModelAssert.Track(Current, V1).Clips[^1].Duration);

        await Ok("edit.move", """{"clipIds":["v2"],"deltaFrames":5}""", id);
        Assert.Equal(105, ModelAssert.Clip(Current, "v2").Start);
        Assert.Equal(105, ModelAssert.Clip(Current, "a2").Start);
        await Ok("edit.trim", """{"clipId":"v2","edge":"end","toFrame":180,"includeLinked":false}""", id);
        Assert.Equal(180, ModelAssert.Clip(Current, "v2").End);
        await Ok("edit.slip", """{"clipId":"v2","deltaFrames":10}""", id);
        Assert.Equal(510 * F, ModelAssert.Media(Current, "v2").SourceInFlicks);
        await Ok("edit.speed", """{"clipIds":["v2"],"percent":200,"rippleFollowing":false}""", id);
        Assert.Equal(200, ModelAssert.Media(Current, "v2").SpeedPercent);
        await Ok("edit.clip-props", """{"clipIds":["v1","a1"],"opacity":0.5,"gainDb":-6,"transform":{"scale":0.5},"color":{"saturation":0}}""", id);
        Assert.Equal(0.5, ModelAssert.Media(Current, "v1").Video.Opacity);
        Assert.Equal(0.5, ModelAssert.Media(Current, "v1").Video.Transform.Scale);
        Assert.Equal(0, ModelAssert.Media(Current, "v1").Video.Color.Saturation);
        Assert.Equal(-6, ModelAssert.Media(Current, "a1").Audio.GainDb);
        var keys = await Ok("edit.volume-keys", """{"clipId":"a1","keys":[{"frame":10,"db":-3},{"frame":500,"db":0}]}""", id);
        Assert.NotEmpty(keys.Warnings);
        Assert.Equal(new VolumeKey(10, -3), Assert.Single(ModelAssert.Media(Current, "a1").Audio.Keys));
        await Ok("edit.unlink", """{"clipIds":["v1"]}""", id);
        Assert.Null(ModelAssert.Clip(Current, "v1").LinkId);
        await Ok("edit.link", """{"clipIds":["v1","a1"]}""", id);
        Assert.NotNull(ModelAssert.Clip(Current, "v1").LinkId);
        await Ok("edit.delete", """{"clipIds":["v1"],"ripple":true}""", id);
        Assert.Null(TimelineQuery.FindClip(Current, "a1"));
        Assert.Equal(5, ModelAssert.Track(Current, V1).Clips[0].Start);
        ModelAssert.Valid(Current);
    }

    [Fact]
    public async Task RemoveRangesAndCloseGapWork()
    {
        var id = await _host.AcquireAsync();
        await Ok("edit.remove-ranges", """{"ranges":[{"start":120,"end":140},{"start":10,"end":20}]}""", id);
        Assert.Equal(170, TimelineQuery.Duration(Current));
        await Ok("edit.remove-ranges", """{"ranges":[{"start":0,"end":10}],"ripple":false,"trackIds":["tr_v1"]}""", id);
        Assert.Equal(10, ModelAssert.Track(Current, V1).Clips[0].Start);
        await Ok("edit.close-gap", """{"trackId":"tr_v1","gapStart":0}""", id);
        Assert.Equal(0, ModelAssert.Track(Current, V1).Clips[0].Start);
        ModelAssert.Valid(Current);
    }

    [Fact]
    public async Task TransitionsTitlesMarkersTracksAndSequenceWork()
    {
        var id = await _host.AcquireAsync();
        await Ok("edit.transition.update", """{"transitionId":"x_1","kind":"dipToBlack","durationFrames":16}""", id);
        var x = ModelAssert.Track(Current, V1).Transitions.Single();
        Assert.Equal(TransitionKind.DipToBlack, x.Kind);
        Assert.Equal(16, x.Duration);
        await Ok("edit.transition.remove", """{"transitionIds":["x_1"]}""", id);
        Assert.Empty(ModelAssert.Track(Current, V1).Transitions);
        await Ok("edit.transition.add", """{"trackId":"tr_a1","cutFrame":100,"durationFrames":8,"kind":"crossfade"}""", id);
        Assert.Equal(TransitionKind.AudioCrossfade, ModelAssert.Track(Current, A1).Transitions.Single().Kind);

        var title = await Ok("edit.title.add", """{"trackId":"tr_v2","at":60,"durationFrames":40,"title":{"text":"Meet Ana","anchor":"top","color":"#FF0000","offsetY":-20}}""", id);
        Assert.Equal("Agent: Add Title", title.UndoLabel);
        var added = ModelAssert.Track(Current, V2).Clips.OfType<TitleClip>().Single(t => t.Title.Text == "Meet Ana");
        Assert.Equal(TitleAnchor.Top, added.Title.Anchor);
        Assert.Equal("#FF0000FF", added.Title.Color);
        Assert.Equal("Inter-Bold", added.Title.Font["builtin:".Length..]);
        await Ok("edit.title.update", $$"""{"clipId":"{{added.Id}}","title":{"offsetY":40},"opacity":0.75}""", id);
        var updated = (TitleClip)ModelAssert.Clip(Current, added.Id);
        Assert.Equal(40, updated.Title.OffsetY);
        Assert.Equal("Meet Ana", updated.Title.Text);
        Assert.Equal(0.75, updated.Opacity);

        await Ok("edit.marker.add", """{"frame":40,"name":"Beat","color":"#00ff00"}""", id);
        var marker = Current.Sequence.Markers.Single(m => m.Name == "Beat");
        Assert.Equal("#00FF00", marker.Color);
        await Ok("edit.marker.update", $$"""{"markerId":"{{marker.Id}}","frame":45}""", id);
        Assert.Equal(45, Current.Sequence.Markers.Single(m => m.Id == marker.Id).Frame);
        await Ok("edit.marker.remove", """{"markerIds":["k_1"]}""", id);
        Assert.DoesNotContain(Current.Sequence.Markers, m => m.Id == "k_1");

        await Ok("edit.in-out", """{"inFrame":10,"outFrame":90}""", id);
        Assert.Equal((10L, 90L), (Current.Sequence.InFrame!.Value, Current.Sequence.OutFrame!.Value));
        await Ok("edit.in-out", """{"outFrame":null}""", id);
        Assert.Equal(10, Current.Sequence.InFrame);
        Assert.Null(Current.Sequence.OutFrame);

        await Ok("edit.track.add", """{"kind":"audio"}""", id);
        Assert.Equal(3, TimelineQuery.TracksOfKind(Current, TrackKind.Audio).Length);
        var gain = await Ok("edit.track.update", """{"trackId":"tr_a2","muted":true,"gainDb":40,"hidden":true}""", id);
        Assert.Equal(2, gain.Warnings.Count);
        Assert.True(ModelAssert.Track(Current, A2).Muted);
        Assert.Equal(TimelineEdits.MaxGainDb, ModelAssert.Track(Current, A2).GainDb);
        await Ok("edit.track.remove", """{"trackId":"tr_a2"}""", id);
        Assert.Null(TimelineQuery.FindTrack(Current, A2));

        await Ok("edit.sequence", """{"frameRate":"30000/1001"}""", id);
        Assert.Equal(Core.Time.FrameRate.Fps2997, Current.Sequence.Settings.Rate);
        await Fails("edit.sequence", """{"frameRate":"12"}""", id, ErrorCode.InvalidArgument);
        ModelAssert.Valid(Current);
    }

    [Fact]
    public async Task IfRevisionDetectsAMovedProject()
    {
        var id = await _host.AcquireAsync();
        var status = await _host.CallAsync("status");
        var revision = status.Result!.Value.GetProperty("revision").GetInt64();
        await Ok("edit.split", $$"""{"frame":50,"ifRevision":{{revision}}}""", id);
        var conflict = await Fails("edit.split", $$"""{"frame":60,"ifRevision":{{revision}}}""", id, ErrorCode.Conflict);
        Assert.Equal("revision_mismatch", conflict.Detail("reason"));
    }

    [Fact]
    public async Task ABatchIsOneUndoStepAndUndoTouchesOnlyAgentSteps()
    {
        // A user step before the grant stays out of the agent's reach.
        _host.Session.Execute(p => TimelineEdits.AddMarker(new SequentialIdGenerator(), p, 5, "user"));
        var id = await _host.AcquireAsync();
        Assert.Equal(1, _host.Lock.UndoBaseline);
        var start = Current;
        await Ok("batch.begin", """{"label":"Remove silences"}""", id);
        await Fails("batch.begin", """{"label":"Again"}""", id, ErrorCode.Conflict);
        await Ok("edit.remove-ranges", """{"ranges":[{"start":10,"end":20}]}""", id);
        await Ok("edit.title.add", """{"trackId":"tr_v2","at":100,"durationFrames":20,"title":{"text":"T"}}""", id);
        Assert.Equal("Remove silences", (await _host.CallAsync("status")).Result!.Value.GetProperty("batch").GetString());
        Assert.Equal("batch_open", (await Fails("undo", "{}", id, ErrorCode.Conflict)).Detail("reason"));
        var end = await Ok("batch.end", "{}", id);
        Assert.Equal("Agent: Remove silences", end.UndoLabel);
        Assert.Equal(2, _host.Session.UndoCount);
        var afterBatch = Current;

        await Ok("undo", "{}", id);
        Assert.Same(start, Current);
        Assert.Equal("not_agent_step", (await Fails("undo", "{}", id, ErrorCode.Conflict)).Detail("reason"));
        await Ok("redo", "{}", id);
        Assert.Same(afterBatch, Current);
        Assert.Equal("not_agent_step", (await Fails("redo", "{}", id, ErrorCode.Conflict)).Detail("reason"));

        // A new agent edit resets the redo counter.
        await Ok("undo", "{}", id);
        await Ok("edit.split", """{"frame":50}""", id);
        await Fails("redo", "{}", id, ErrorCode.Conflict);
    }

    [Fact]
    public async Task CancelBatchRevertsEverythingSinceBegin()
    {
        var id = await _host.AcquireAsync();
        var start = Current;
        await Ok("batch.begin", """{"label":"Try"}""", id);
        await Ok("edit.split", """{"frame":50}""", id);
        await Ok("batch.cancel", "{}", id);
        Assert.Same(start, Current);
        Assert.Equal(0, _host.Session.UndoCount);
        await Fails("batch.end", "{}", id, ErrorCode.Conflict);
    }

    [Fact]
    public async Task ReleaseCommitsAnOpenBatchAndLaterCallsAreLost()
    {
        var id = await _host.AcquireAsync();
        await Ok("batch.begin", """{"label":"Cut"}""", id);
        await Ok("edit.split", """{"frame":50}""", id);
        await Ok("lock.release", "{}", id);
        Assert.False(_host.Session.InGroup);
        Assert.Equal("Agent: Cut", _host.Session.UndoLabel);
        var lost = await Fails("edit.split", """{"frame":60}""", id, ErrorCode.LockLost);
        Assert.Equal("released", lost.Detail("reason"));
    }

    [Fact]
    public async Task TakeOverMakesTheNextCallLockLost()
    {
        var id = await _host.AcquireAsync();
        await _host.Ui.InvokeAsync(() => _host.Lock.Revoke(_host.Time.NowMs), CancellationToken.None);
        var lost = await Fails("edit.split", """{"frame":60}""", id, ErrorCode.LockLost);
        Assert.Equal("revoked_by_user", lost.Detail("reason"));
        Assert.Contains("took over", lost.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APausedLockMakesMutatingCallsWaitThenFailWhileReadsWork()
    {
        var id = await _host.AcquireAsync();
        await _host.Ui.InvokeAsync(() => _host.Lock.Pause(_host.Time.NowMs), CancellationToken.None);
        Assert.Null((await _host.CallAsync("project.get", "{}", id)).Error);
        Assert.Null((await _host.CallAsync("lock.renew", "{}", id)).Error);
        var paused = await Fails("edit.split", """{"frame":50}""", id, ErrorCode.Paused);
        Assert.Equal(2000, paused.RetryAfterMs);

        // Resumed while waiting: the call runs.
        var call = _host.CallAsync("edit.split", """{"frame":50}""", id);
        await Task.Delay(50);
        await _host.Ui.InvokeAsync(() => _host.Lock.Resume(_host.Time.NowMs), CancellationToken.None);
        var r = await call;
        Assert.Null(r.Error);
        Assert.True(r.Changed);
    }

    [Fact]
    public async Task TheLockToolsReportTheState()
    {
        var granted = await _host.CallAsync("lock.acquire", """{"agentName":"Claude Code","reason":"Cut","leaseSeconds":60}""");
        var id = granted.Result!.Value.GetProperty("lockId").GetString();
        Assert.Equal(60, granted.Result.Value.GetProperty("leaseSeconds").GetInt32());
        Assert.Equal("held", granted.Result.Value.GetProperty("phase").GetString());
        var mine = await _host.CallAsync("lock.get", "{}", id);
        Assert.True(mine.Result!.Value.GetProperty("mine").GetBoolean());
        var other = await _host.CallAsync("lock.get");
        Assert.False(other.Result!.Value.GetProperty("mine").GetBoolean());
        var busy = await _host.CallAsync("lock.acquire", """{"agentName":"Cursor","reason":"x"}""");
        Assert.Equal(ErrorCode.Busy, busy.Error!.Code);
        Assert.Equal("held_by_agent", busy.Error.Detail("reason"));
        Assert.NotEmpty(_host.Audit.Snapshot());
        Assert.Contains(_host.Audit.Snapshot(), e => e.Tool == "lock.acquire" && e.Agent == "Claude Code");
    }

    [Fact]
    public async Task AcquireWaitsForABusyUser()
    {
        _host.SetUser(new Cutlass.Agent.Hosting.AgentUserState(true, false, false, false));
        var call = _host.CallAsync("lock.acquire", """{"agentName":"A","reason":"B","waitMs":5000}""");
        await Task.Delay(50);
        Assert.False(call.IsCompleted);
        Assert.Equal(LockPhase.Pending, _host.Lock.State.Phase);
        _host.SetUser(Cutlass.Agent.Hosting.AgentUserState.Idle);
        var r = await call;
        Assert.Null(r.Error);
        Assert.Equal(1, _host.Prepared);
    }

    [Fact]
    public async Task ProjectNewRefusesUnsavedChangesInTheAppButNotHeadlessWithDiscard()
    {
        var id = await _host.AcquireAsync();
        await Ok("edit.split", """{"frame":50}""", id);
        _host.Policy = _host.Policy with { AcceptsDiscardChanges = false };
        Assert.Equal("unsaved_changes", (await Fails("project.new", """{"discardChanges":true}""", id, ErrorCode.Conflict)).Detail("reason"));
        _host.Policy = _host.Policy with { AcceptsDiscardChanges = true };
        var r = await Ok("project.new", """{"discardChanges":true,"width":1280,"height":720,"frameRate":"25"}""", id);
        Assert.Equal(1280, r.Result!.Value.GetProperty("sequence").GetProperty("width").GetInt32());
        Assert.Equal(0, _host.Session.UndoCount);
        Assert.Equal(0, _host.Lock.UndoBaseline);
    }

    [Fact]
    public async Task SaveAsWritesInsideTheWriteFoldersOnly()
    {
        var id = await _host.AcquireAsync();
        await Fails("project.save", "{}", id, ErrorCode.Conflict);
        var target = Path.Combine(_host.Root, "out", "edit");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var r = await Ok("project.save-as", JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = target }), id);
        Assert.EndsWith("edit.cutlass", r.Result!.Value.GetProperty("path").GetString(), StringComparison.Ordinal);
        Assert.True(File.Exists(target + ".cutlass"));
        Assert.False(_host.Session.IsDirty);
        await Ok("project.save", "{}", id);
        var other = Path.Combine(_host.Root, "out", "other.cutlass");
        await File.WriteAllTextAsync(other, "taken");
        Assert.Equal("exists", (await Fails("project.save-as", JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = other }), id, ErrorCode.Conflict)).Detail("reason"));
        var outside = Path.Combine(Path.GetTempPath(), "cutlass-not-allowed-" + Guid.NewGuid().ToString("N"), "x.cutlass");
        Assert.Equal(ErrorCode.Forbidden, (await _host.CallAsync("project.save-as", JsonSerializer.Serialize(new Dictionary<string, string> { ["path"] = outside }), id)).Error!.Code);
    }

    [Fact]
    public async Task ThePlayheadToolMovesTheUserPlayheadAndRendersNeedFfmpeg()
    {
        var id = await _host.AcquireAsync();
        await Ok("playhead.set", """{"frame":42}""", id);
        Assert.Equal(42, _host.Playhead);
        Assert.Equal([42L], _host.Focus);
        await Fails("frame.render", """{"frame":0}""", id, ErrorCode.Unavailable);
        await Fails("audio.levels", """{"start":0,"end":10}""", id, ErrorCode.Unavailable);
        await Fails("media.import", """{"paths":["C:/x.mp4"]}""", id, ErrorCode.Unavailable);
    }

    [Fact]
    public async Task EventsReportProjectAndLockChanges()
    {
        var id = await _host.AcquireAsync();
        await Ok("edit.split", """{"frame":50}""", id);
        var events = await _host.CallAsync("events", """{"after":0,"timeoutMs":0}""");
        var list = events.Result!.Value.GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(list, e => e.GetProperty("kind").GetString() == "lockChanged" && e.GetProperty("phase").GetString() == "held");
        Assert.Contains(list, e => e.GetProperty("kind").GetString() == "projectChanged" && e.GetProperty("by").GetString() == "agent");
    }
}
