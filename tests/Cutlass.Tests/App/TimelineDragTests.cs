using System.Collections.Immutable;
using Cutlass.App.Controls.Drag;
using Cutlass.App.Controls.Timeline;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

/// <summary>The M6 drags (DragOperationTests pattern): roll, slip, fades, keyframes, volume line, markers, transitions.</summary>
public sealed class TimelineDragTests
{
    private readonly ProjectSession _session;
    private readonly TimelineViewModel _vm;

    public TimelineDragTests()
    {
        var keyed = A("a1", 0, 50, link: "l1") with
        {
            Audio = AudioProps.Default with { Keys = [new VolumeKey(10, 0), new VolumeKey(30, -6)] },
        };
        var project = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, link: "l1"), V("c2", 50, 50, sourceFrame: 100, link: "l2"))
            .With(A1, keyed, A("a2", 50, 50, sourceFrame: 100, link: "l2"))
            .With(V2, V("t1", 200, 50), V("t2", 250, 50, sourceFrame: 100))
            .Transition(V2, "x1", "t1", "t2", 10)
            .Marker("k1", 20)
            .Build();
        _session = new ProjectSession(project, () => 0);
        _vm = new TimelineViewModel(_session, new SequentialIdGenerator(1000));
        _vm.ViewState.SetViewport(1000, 400);
    }

    private static double X(double frame) => TimelineViewState.HeaderWidth + (frame * TimelineViewState.DefaultPixelsPerFrame);

    private double Y(string trackId, double yInRow = 28) => _vm.ViewState.ToControlY(_vm.GetLayout().Row(trackId)!.Top) + yInRow;

    private DragContext Press(double frame, string trackId, bool shift = false, bool alt = false, double yInRow = 28)
    {
        var x = X(frame);
        var y = Y(trackId, yInRow);
        var hit = HitTester.Hit(_session.Current, _vm.ViewState, _vm.GetLayout(), x, y, _vm.Playhead, _vm.SelectedClipIds);
        return new DragContext(_vm, hit, x, y, false, shift, alt);
    }

    private static void Run(IDragOperation drag, DragContext c, double toFrame, double dy = 0)
    {
        drag.Begin(c);
        c.MoveTo(X(toFrame), c.Y + dy);
        drag.Update(c);
    }

    private static void MoveTo(IDragOperation drag, DragContext c, double toFrame, double dy = 0)
    {
        c.MoveTo(X(toFrame), c.PressY + dy);
        drag.Update(c);
    }

    private Clip Clip(string id) => TimelineQuery.FindClip(_session.Current, id)!.Value.Clip;

    private ImmutableArray<VolumeKey> Keys(string id) => ((MediaClip)Clip(id)).Audio.Keys;

    // One dB in pixels on a 48 px audio row (the clip is 46 px tall).
    private static double DbPixels(double db) => -db * 46 / 72.0;

    [Fact]
    public void CutAtFindsTheButtingNeighbour()
    {
        var project = _session.Current;
        Assert.Equal(("c1", "c2"), RollDrag.CutAt(project, "c1", ClipEdge.End));
        Assert.Equal(("c1", "c2"), RollDrag.CutAt(project, "c2", ClipEdge.Start));
        Assert.Null(RollDrag.CutAt(project, "c1", ClipEdge.Start));
        Assert.Null(RollDrag.CutAt(project, "c2", ClipEdge.End));
    }

    [Fact]
    public void RollMovesTheCutAndItsLinkedCutAsOneStep()
    {
        var drag = new RollDrag("c1", "c2");
        var c = Press(49.5, V1);
        Run(drag, c, 59.5);
        Assert.Equal(60, Clip("c1").Duration);
        Assert.Equal(60, Clip("c2").Start);
        Assert.Equal(40, Clip("c2").Duration);
        Assert.Equal(110 * F, ((MediaClip)Clip("c2")).SourceInFlicks);
        Assert.Equal(60, Clip("a2").Start);
        Assert.Equal(new ViewerHint(59, 60), drag.Hint);
        Assert.Equal("+00:00:00:10", drag.Tooltip);
        Assert.False(drag.TooltipIsLimit);
        Assert.Equal(0, _session.UndoCount);
        drag.Commit();
        Assert.Equal(1, _session.UndoCount);
        Assert.Equal("Roll", _session.UndoLabel);
    }

    [Fact]
    public void RollWithAltLeavesThePartnersAndStopsAtTheHandles()
    {
        var drag = new RollDrag("c1", "c2");
        var c = Press(49.5, V1, alt: true);
        Run(drag, c, -100);
        Assert.Equal(1, Clip("c1").Duration);
        Assert.True(drag.TooltipIsLimit);
        Assert.Equal(50, Clip("a2").Start);
        drag.Cancel();
        Assert.Equal(50, Clip("c1").Duration);
        Assert.Equal(0, _session.UndoCount);
    }

    [Fact]
    public void SlipShiftsTheSourceAndKeepsThePosition()
    {
        var drag = new SlipDrag("c2");
        var c = Press(70, V1);
        Run(drag, c, 80);
        var c2 = (MediaClip)Clip("c2");
        Assert.Equal(50, c2.Start);
        Assert.Equal(50, c2.Duration);
        Assert.Equal(90 * F, c2.SourceInFlicks);
        Assert.Equal(90 * F, ((MediaClip)Clip("a2")).SourceInFlicks);
        Assert.Equal(new ViewerHint(50, 99), drag.Hint);
        Assert.False(drag.TooltipIsLimit);
        MoveTo(drag, c, 270);
        Assert.Equal(0, ((MediaClip)Clip("c2")).SourceInFlicks);
        Assert.True(drag.TooltipIsLimit);
        drag.Commit();
        Assert.Equal("Slip", _session.UndoLabel);
        Assert.Equal(1, _session.UndoCount);
    }

    [Fact]
    public void SlipWithAltSlipsOneSide()
    {
        var drag = new SlipDrag("c2");
        Run(drag, Press(70, V1, alt: true), 60);
        Assert.Equal(110 * F, ((MediaClip)Clip("c2")).SourceInFlicks);
        Assert.Equal(100 * F, ((MediaClip)Clip("a2")).SourceInFlicks);
    }

    [Fact]
    public void FadeInHandleSetsTheAudioFadeAndClamps()
    {
        var drag = new FadeHandleDrag("a2", fadeIn: true);
        var c = Press(51, A1, shift: true, yInRow: 4);
        Run(drag, c, 63);
        Assert.Equal(12, ((MediaClip)Clip("a2")).Audio.FadeInFrames);
        Assert.Equal("Fade in 00:00:00:12", drag.Tooltip);
        Assert.False(drag.TooltipIsLimit);
        MoveTo(drag, c, 200);
        Assert.Equal(50, ((MediaClip)Clip("a2")).Audio.FadeInFrames);
        Assert.True(drag.TooltipIsLimit);
        drag.Commit();
        Assert.Equal("Fade In", _session.UndoLabel);
        Assert.Equal(1, _session.UndoCount);
    }

    [Fact]
    public void FadeOutHandleSetsTheVideoOpacityFade()
    {
        var drag = new FadeHandleDrag("c2", fadeIn: false);
        var c = Press(99, V1, shift: true, yInRow: 4);
        Run(drag, c, 92);
        Assert.Equal(7, ((MediaClip)Clip("c2")).Video.FadeOutFrames);
        Assert.Equal(0, ((MediaClip)Clip("c2")).Audio.FadeOutFrames);
        MoveTo(drag, c, 120);
        Assert.Equal(0, ((MediaClip)Clip("c2")).Video.FadeOutFrames);
        Assert.True(drag.TooltipIsLimit);
        drag.Cancel();
        Assert.Equal(0, _session.UndoCount);
    }

    [Fact]
    public void FadesAreCappedByTheOtherFade()
    {
        _session.Execute(p => TimelineEdits.UpdateClips(p, ["c1"], cl => FadeHandleDrag.WithFade(cl, false, 20), "Fade Out"));
        var drag = new FadeHandleDrag("c1", fadeIn: true);
        Run(drag, Press(1, V1, shift: true, yInRow: 4), 45);
        Assert.Equal(30, ((MediaClip)Clip("c1")).Video.FadeInFrames);
        Assert.True(drag.TooltipIsLimit);
        Assert.IsType<TitleClip>(FadeHandleDrag.WithFade(Title("t", 0, 10), true, 3));
        Assert.Equal(3, ((TitleClip)FadeHandleDrag.WithFade(Title("t", 0, 10), true, 3)).FadeInFrames);
    }

    [Fact]
    public void KeyframeDragMovesTimeAndLevelWithinItsNeighbours()
    {
        _vm.Select(["a1"], false);
        var drag = new KeyframeDrag("a1", 0);
        var c = Press(10.5, A1);
        Run(drag, c, 15.5, DbPixels(6));
        Assert.Equal(new VolumeKey(15, 6), Keys("a1")[0]);
        Assert.Equal(new VolumeKeyRef("a1", 10), _vm.SelectedVolumeKey);
        Assert.Equal("+6.0 dB  00:00:00:15", drag.Tooltip);
        MoveTo(drag, c, 80.5, DbPixels(6));
        Assert.Equal(29, Keys("a1")[0].Frame);
        Assert.True(drag.TooltipIsLimit);
        MoveTo(drag, c, 15.5, DbPixels(100));
        Assert.Equal(12, Keys("a1")[0].Db);
        drag.Commit();
        Assert.Equal("Move Keyframe", _session.UndoLabel);
        Assert.Equal(1, _session.UndoCount);
        Assert.Equal(new VolumeKeyRef("a1", 15), _vm.SelectedVolumeKey);
    }

    [Fact]
    public void ShiftConstrainsAKeyframeDragToOneAxis()
    {
        _vm.Select(["a1"], false);
        var drag = new KeyframeDrag("a1", 1);
        var c = Press(30.5, A1, shift: true);
        Run(drag, c, 35.5, DbPixels(3));
        Assert.Equal(new VolumeKey(35, -6), Keys("a1")[1]);
        MoveTo(drag, c, 31.5, DbPixels(6));
        Assert.Equal(new VolumeKey(30, 0), Keys("a1")[1]);
    }

    [Fact]
    public void VolumeLineChangesTheClipGainWithoutKeys()
    {
        _vm.Select(["a2"], false);
        var drag = new VolumeLineDrag("a2", 20);
        var c = Press(70, A1);
        Run(drag, c, 70, DbPixels(6));
        Assert.Empty(drag.SegmentKeys);
        Assert.Equal(6, ((MediaClip)Clip("a2")).Audio.GainDb, 6);
        Assert.Equal("+6.0 dB", drag.Tooltip);
        c.Shift = true;
        drag.Update(c);
        Assert.Equal(0.6, ((MediaClip)Clip("a2")).Audio.GainDb, 6);
        MoveTo(drag, c, 70, DbPixels(-200));
        Assert.Equal(-20, ((MediaClip)Clip("a2")).Audio.GainDb, 6);
        c.Shift = false;
        drag.Update(c);
        Assert.Equal(-60, ((MediaClip)Clip("a2")).Audio.GainDb, 6);
        Assert.True(drag.TooltipIsLimit);
        drag.Commit();
        Assert.Equal("Clip Gain", _session.UndoLabel);
    }

    [Fact]
    public void VolumeLineBetweenKeysMovesTheSegment()
    {
        _vm.Select(["a1"], false);
        var drag = new VolumeLineDrag("a1", 20);
        Run(drag, Press(20, A1), 20, DbPixels(6));
        Assert.Equal(new[] { 0, 1 }, drag.SegmentKeys);
        Assert.Equal(new[] { new VolumeKey(10, 6), new VolumeKey(30, 0) }, Keys("a1"));
        drag.Commit();
        Assert.Equal("Volume", _session.UndoLabel);
        var before = new VolumeLineDrag("a1", 2);
        Run(before, Press(2, A1), 2, DbPixels(-6));
        Assert.Equal(new[] { 0 }, before.SegmentKeys);
        Assert.Equal(new[] { new VolumeKey(10, 0), new VolumeKey(30, 0) }, Keys("a1"));
        before.Commit();
    }

    [Fact]
    public void MarkerDragMovesTheMarkerAndClampsAtZero()
    {
        var drag = new MarkerDrag("k1");
        var c = new DragContext(_vm, HitResult.Nothing, X(20), 20, false, false, false);
        Run(drag, c, 35);
        Assert.Equal(35, _session.Current.Sequence.Markers.Single().Frame);
        Assert.Equal("00:00:01:10", drag.Tooltip);
        MoveTo(drag, c, -100);
        Assert.Equal(0, _session.Current.Sequence.Markers.Single().Frame);
        Assert.True(drag.TooltipIsLimit);
        MoveTo(drag, c, 48);
        Assert.Equal(50, _session.Current.Sequence.Markers.Single().Frame);
        Assert.Equal(50, drag.SnapFrame);
        drag.Commit();
        Assert.Equal("Move Marker", _session.UndoLabel);
        Assert.Equal(1, _session.UndoCount);
    }

    [Fact]
    public void TransitionEndsChangeTheCentredDuration()
    {
        var right = new TransitionDrag("x1", rightEnd: true);
        var c = Press(254.5, V2);
        Run(right, c, 259.5);
        Assert.Equal("x1", _vm.SelectedTransitionId);
        Assert.Equal(20, TimelineQuery.FindTransition(_session.Current, "x1")!.Value.Transition.Duration);
        Assert.Equal("Dur 00:00:00:20", right.Tooltip);
        MoveTo(right, c, 400);
        Assert.True(right.TooltipIsLimit);
        right.Commit();
        Assert.Equal("Transition Duration", _session.UndoLabel);

        var left = new TransitionDrag("x1", rightEnd: false);
        var d = Press(240, V2);
        _session.Undo();
        Run(left, d, 243);
        Assert.Equal(4, TimelineQuery.FindTransition(_session.Current, "x1")!.Value.Transition.Duration);
        MoveTo(left, d, 260);
        Assert.Equal(2, TimelineQuery.FindTransition(_session.Current, "x1")!.Value.Transition.Duration);
        Assert.True(left.TooltipIsLimit);
        left.Cancel();
        Assert.Equal(10, TimelineQuery.FindTransition(_session.Current, "x1")!.Value.Transition.Duration);
    }
}
