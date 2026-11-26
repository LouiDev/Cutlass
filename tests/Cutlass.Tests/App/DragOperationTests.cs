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

public sealed class DragOperationTests
{
    private readonly ProjectSession _session;
    private readonly TimelineViewModel _vm;

    public DragOperationTests()
    {
        var project = new ProjectBuilder()
            .With(V1, V("v1", 0, 50, link: "l1"), V("v2", 100, 50, link: "l2"))
            .With(A1, A("a1", 0, 50, link: "l1"), A("a2", 100, 50, link: "l2"))
            .Build();
        _session = new ProjectSession(project, () => 0);
        _vm = new TimelineViewModel(_session, new SequentialIdGenerator(1000));
        _vm.ViewState.SetViewport(1000, 400);
    }

    private static double X(double frame) => TimelineViewState.HeaderWidth + (frame * TimelineViewState.DefaultPixelsPerFrame);

    private double Y(string trackId) => _vm.ViewState.ToControlY(_vm.GetLayout().Row(trackId)!.Top) + 28;

    private DragContext Press(double frame, string trackId, bool ctrl = false, bool shift = false, bool alt = false)
    {
        var x = X(frame);
        var y = Y(trackId);
        var hit = HitTester.Hit(_session.Current, _vm.ViewState, _vm.GetLayout(), x, y, _vm.Playhead);
        return new DragContext(_vm, hit, x, y, ctrl, shift, alt);
    }

    private static void Run(IDragOperation drag, DragContext c, double toFrame, double? toY = null)
    {
        drag.Begin(c);
        c.MoveTo(X(toFrame), toY ?? c.Y);
        drag.Update(c);
    }

    private Clip Clip(string id) => TimelineQuery.FindClip(_session.Current, id)!.Value.Clip;

    private string TrackOf(string id) => TimelineQuery.FindClip(_session.Current, id)!.Value.Track.Id;

    [Fact]
    public void MoveRecordsExactlyOneUndoStep()
    {
        _vm.Select(_vm.ExpandLinked(["v2"], false), false);
        var drag = new MoveClipsDrag("v2");
        var c = Press(110, V1);
        Run(drag, c, 130);
        Assert.Equal(120, Clip("v2").Start);
        Assert.Equal(120, Clip("a2").Start);
        Assert.Equal("+00:00:00:20", drag.Tooltip);
        Assert.False(drag.TooltipIsLimit);
        Assert.NotNull(drag.OriginProject);
        Assert.Equal(0, _session.UndoCount);
        c.MoveTo(X(140), c.Y);
        drag.Update(c);
        drag.Commit();
        Assert.Equal(130, Clip("v2").Start);
        Assert.Equal(1, _session.UndoCount);
        Assert.Equal("Move", _session.UndoLabel);
    }

    [Fact]
    public void MoveSnapsToEdgesUnlessShiftIsHeld()
    {
        _vm.Select(["v2"], false);
        var drag = new MoveClipsDrag("v2");
        var c = Press(110, V1, alt: true);
        Run(drag, c, 64);
        Assert.Equal(50, Clip("v2").Start);
        Assert.Equal(50, drag.SnapFrame);
        c.Shift = true;
        drag.Update(c);
        Assert.Equal(54, Clip("v2").Start);
        Assert.Null(drag.SnapFrame);
        drag.Commit();
    }

    [Fact]
    public void CancelRestoresTheGestureStart()
    {
        var origin = _session.Current;
        _vm.Select(["v2", "a2"], false);
        var drag = new MoveClipsDrag("v2");
        Run(drag, Press(110, V1), 200);
        Assert.NotSame(origin, _session.Current);
        drag.Cancel();
        Assert.Same(origin, _session.Current);
        Assert.Equal(0, _session.UndoCount);
    }

    [Fact]
    public void CtrlAtDropInserts()
    {
        _vm.Select(["v2", "a2"], false);
        var drag = new MoveClipsDrag("v2");
        var c = Press(110, V1);
        drag.Begin(c);
        c.Ctrl = true;
        c.MoveTo(X(185), c.Y);
        drag.Update(c);
        Assert.True(drag.IsInsert);
        drag.Commit();
        Assert.Equal(175, Clip("v2").Start);
        Assert.Equal("Move (Insert)", _session.UndoLabel);
    }

    [Fact]
    public void AltMovesOnlyThePressedClipAndVerticalMovesStayInTheKind()
    {
        _vm.Select(["v2", "a2"], false);
        var drag = new MoveClipsDrag("v2");
        Run(drag, Press(110, V1, alt: true), 110, Y(V2));
        drag.Commit();
        Assert.Equal(V2, TrackOf("v2"));
        Assert.Equal(A1, TrackOf("a2"));

        _vm.Select(["v1", "a1"], false);
        var second = new MoveClipsDrag("v1");
        Run(second, Press(10, V1), 10, Y(A2));
        second.Commit();
        Assert.Equal(V1, TrackOf("v1"));
        Assert.Equal(A1, TrackOf("a1"));
    }

    [Fact]
    public void LinkedPartnersFollowTheTrackOffset()
    {
        _vm.Select(["v1", "a1"], false);
        var drag = new MoveClipsDrag("v1");
        Run(drag, Press(10, V1), 10, Y(V2));
        drag.Commit();
        Assert.Equal(V2, TrackOf("v1"));
        Assert.Equal(A2, TrackOf("a1"));
    }

    [Fact]
    public void TrimEndTrimsTheLinkedPartnerAndShowsTheDuration()
    {
        var drag = new TrimDrag("v1", ClipEdge.End, ripple: false);
        Run(drag, Press(49.5, V1), 39.5);
        Assert.Equal(40, Clip("v1").Duration);
        Assert.Equal(40, Clip("a1").Duration);
        Assert.Equal("-00:00:00:10  Dur 00:00:01:15", drag.Tooltip);
        Assert.Equal(new ViewerHint(39), drag.Hint);
        drag.Commit();
        Assert.Equal("Trim", _session.UndoLabel);
        Assert.Equal(1, _session.UndoCount);
    }

    [Fact]
    public void TrimStopsAtTheNeighbourAndFlagsTheLimit()
    {
        var drag = new TrimDrag("v1", ClipEdge.End, ripple: false);
        Run(drag, Press(49.5, V1, shift: true), 129.5);
        Assert.Equal(100, Clip("v1").End);
        Assert.True(drag.TooltipIsLimit);
        Assert.Null(drag.SnapFrame);
        drag.Cancel();
        Assert.Equal(50, Clip("v1").End);
    }

    [Fact]
    public void AltAtPressTrimsOneSide()
    {
        var drag = new TrimDrag("v1", ClipEdge.End, ripple: false);
        Run(drag, Press(49.5, V1, alt: true), 39.5);
        drag.Commit();
        Assert.Equal(40, Clip("v1").Duration);
        Assert.Equal(50, Clip("a1").Duration);
    }

    [Fact]
    public void RippleTrimShiftsLaterClips()
    {
        var drag = new TrimDrag("v1", ClipEdge.End, ripple: true);
        Run(drag, Press(49.5, V1), 39.5);
        drag.Commit();
        Assert.Equal(90, Clip("v2").Start);
        Assert.Equal(90, Clip("a2").Start);
        Assert.Equal("Ripple Trim", _session.UndoLabel);
    }

    [Fact]
    public void RippleStartTrimMeasuresTheDurationChange()
    {
        var drag = new TrimDrag("v2", ClipEdge.Start, ripple: true);
        Run(drag, Press(100.5, V1, shift: true), 110.5);
        Assert.Equal(100, Clip("v2").Start);
        Assert.Equal(40, Clip("v2").Duration);
        Assert.StartsWith("+00:00:00:10", drag.Tooltip, StringComparison.Ordinal);
        drag.Commit();
    }

    [Fact]
    public void MarqueeSelectsTouchedClipsAndCancelRestores()
    {
        _vm.Select(["v2"], false);
        var drag = new MarqueeDrag();
        Run(drag, Press(40, V1), 105, Y(A1));
        Assert.Equal(["a1", "a2", "v1", "v2"], _vm.SelectedClipIds.Order().ToArray());
        drag.Cancel();
        Assert.Equal(["v2"], _vm.SelectedClipIds.ToArray());

        var single = new MarqueeDrag();
        var c = Press(60, V1);
        c.Alt = true;
        Run(single, c, 40);
        Assert.Equal(["v1"], _vm.SelectedClipIds.ToArray());
        single.Commit();
        Assert.Equal(0, _session.UndoCount);
    }

    [Fact]
    public void ShiftMarqueeAddsToTheSelection()
    {
        _vm.Select(["v2"], false);
        var drag = new MarqueeDrag();
        Run(drag, Press(60, V1, shift: true), 40);
        Assert.Equal(["a1", "v1", "v2"], _vm.SelectedClipIds.Order().ToArray());
    }

    [Fact]
    public void ScrubSnapsToEditPointsAndCancelRestores()
    {
        var drag = new ScrubDrag();
        var c = new DragContext(_vm, HitResult.Nothing, X(77.3), 10, false, false, false);
        drag.Begin(c);
        drag.Update(c);
        Assert.Equal(77, _vm.Playhead);
        c.MoveTo(X(98), 10);
        drag.Update(c);
        Assert.Equal(100, _vm.Playhead);
        Assert.Equal(100, drag.SnapFrame);
        c.Shift = true;
        drag.Update(c);
        Assert.Equal(98, _vm.Playhead);
        drag.Cancel();
        Assert.Equal(0, _vm.Playhead);
    }

    [Fact]
    public void PlayheadDragKeepsTheOffset()
    {
        _vm.Playhead = 20;
        var drag = new PlayheadDrag();
        var c = new DragContext(_vm, HitResult.Nothing, X(21.4), Y(V1), false, true, false);
        Run(drag, c, 26.4);
        Assert.Equal(25, _vm.Playhead);
        drag.Commit();
        Assert.Equal(0, _session.UndoCount);
    }

    [Fact]
    public void TrackResizeDragClampsAndCancels()
    {
        var drag = new TrackResizeDrag(V1, TrackKind.Video);
        var c = Press(10, V1);
        drag.Begin(c);
        c.MoveTo(c.X, c.Y + 30);
        drag.Update(c);
        Assert.Equal(86, _vm.ViewState.TrackHeight(V1, TrackKind.Video));
        c.MoveTo(c.X, c.Y + 1000);
        drag.Update(c);
        Assert.Equal(TimelineViewState.MaxTrackHeight, _vm.ViewState.TrackHeight(V1, TrackKind.Video));
        drag.Cancel();
        Assert.Equal(56, _vm.ViewState.TrackHeight(V1, TrackKind.Video));
    }
}
