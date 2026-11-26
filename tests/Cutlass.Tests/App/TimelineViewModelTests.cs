using Cutlass.App.Controls.Timeline;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

public sealed class TimelineViewModelTests
{
    private static TimelineViewModel Vm(ProjectBuilder builder, out ProjectSession session)
    {
        session = new ProjectSession(builder.Build(), () => 0);
        var vm = new TimelineViewModel(session, new SequentialIdGenerator(1000));
        vm.ViewState.SetViewport(1000, 400);
        return vm;
    }

    private static ProjectBuilder Linked() => new ProjectBuilder()
        .With(V1, V("v1", 0, 50, link: "l1"), V("v2", 100, 50, link: "l2"))
        .With(A1, A("a1", 0, 50, link: "l1"), A("a2", 100, 50, link: "l2"));

    [Fact]
    public void ContractDefaults()
    {
        var vm = Vm(Linked(), out var session);
        Assert.Same(session, vm.Session);
        Assert.Equal(EditTool.Select, vm.Tool);
        Assert.True(vm.SnapEnabled);
        Assert.True(vm.LinkedSelection);
        Assert.Empty(vm.SelectedClipIds);
        Assert.Equal(150, vm.SequenceEnd);
        Assert.Null(vm.InPoint);
    }

    [Fact]
    public void PlayheadClampsToTheSequence()
    {
        var vm = Vm(Linked(), out _);
        vm.Playhead = 500;
        Assert.Equal(150, vm.Playhead);
        vm.Playhead = -3;
        Assert.Equal(0, vm.Playhead);
        vm.StepBackwardCommand.Execute(null);
        Assert.Equal(0, vm.Playhead);
        vm.StepForwardManyCommand.Execute(null);
        Assert.Equal(10, vm.Playhead);
        vm.GoToEndCommand.Execute(null);
        Assert.Equal(150, vm.Playhead);
        vm.GoToStartCommand.Execute(null);
        Assert.Equal(0, vm.Playhead);
    }

    [Fact]
    public void EditPointNavigation()
    {
        var vm = Vm(Linked(), out _);
        vm.NextEditCommand.Execute(null);
        Assert.Equal(50, vm.Playhead);
        vm.NextEditCommand.Execute(null);
        Assert.Equal(100, vm.Playhead);
        vm.PreviousEditCommand.Execute(null);
        Assert.Equal(50, vm.Playhead);
    }

    [Fact]
    public void SelectReplacesTheSetAndRaisesSelectionChanged()
    {
        var vm = Vm(Linked(), out _);
        var raised = 0;
        vm.SelectionChanged += (_, _) => raised++;
        vm.Select(["v1"], false);
        var first = vm.SelectedClipIds;
        vm.Select(["v2"], true);
        Assert.NotSame(first, vm.SelectedClipIds);
        Assert.Equal(["v1", "v2"], vm.SelectedClipIds.Order().ToArray());
        vm.Select(["v2"], true);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void LockedAndMissingClipsAreNotSelectable()
    {
        var vm = Vm(Linked().Lock(A1), out _);
        vm.Select(["v1", "a1", "nope"], false);
        Assert.Equal(["v1"], vm.SelectedClipIds.ToArray());
        vm.SelectAllCommand.Execute(null);
        Assert.Equal(["v1", "v2"], vm.SelectedClipIds.Order().ToArray());
    }

    [Fact]
    public void ExpandLinkedHonoursLinkedSelectionAndAlt()
    {
        var vm = Vm(Linked(), out _);
        Assert.Equal(["v1", "a1"], vm.ExpandLinked(["v1"], false).ToArray());
        Assert.Equal(["v1"], vm.ExpandLinked(["v1"], true).ToArray());
        vm.ToggleLinkedSelectionCommand.Execute(null);
        Assert.Equal(["v1"], vm.ExpandLinked(["v1"], false).ToArray());
    }

    [Fact]
    public void ToggleSelectionAddsThenRemoves()
    {
        var vm = Vm(Linked(), out _);
        vm.ToggleSelection(["v1", "a1"]);
        Assert.Equal(2, vm.SelectedClipIds.Count);
        vm.ToggleSelection(["v1", "a1"]);
        Assert.Empty(vm.SelectedClipIds);
    }

    [Fact]
    public void DeleteLiftsAndUndoRestoresWithoutTheSelection()
    {
        var vm = Vm(Linked(), out var session);
        vm.Select(["v1", "a1"], false);
        vm.DeleteSelectionCommand.Execute(null);
        Assert.Null(TimelineQuery.FindClip(session.Current, "v1"));
        Assert.Equal(100, TimelineQuery.FindClip(session.Current, "v2")!.Value.Clip.Start);
        Assert.Empty(vm.SelectedClipIds);
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);
        Assert.NotNull(TimelineQuery.FindClip(session.Current, "v1"));
        Assert.True(vm.RedoCommand.CanExecute(null));
        vm.RedoCommand.Execute(null);
        Assert.Null(TimelineQuery.FindClip(session.Current, "v1"));
    }

    [Fact]
    public void RippleDeleteClosesTheSpace()
    {
        var vm = Vm(Linked(), out var session);
        vm.Select(["v1", "a1"], false);
        vm.RippleDeleteSelectionCommand.Execute(null);
        Assert.Equal(50, TimelineQuery.FindClip(session.Current, "v2")!.Value.Clip.Start);
        Assert.Equal(50, TimelineQuery.FindClip(session.Current, "a2")!.Value.Clip.Start);
    }

    [Fact]
    public void SelectedGapIsClosedByDelete()
    {
        var vm = Vm(Linked(), out var session);
        vm.SelectGap(V1, 50, 100);
        Assert.Equal(new TimelineGap(V1, 50, 100), vm.SelectedGap);
        Assert.Empty(vm.SelectedClipIds);
        vm.DeleteSelectionCommand.Execute(null);
        Assert.Equal(50, TimelineQuery.FindClip(session.Current, "v2")!.Value.Clip.Start);
        Assert.Null(vm.SelectedGap);
    }

    [Fact]
    public void SelectedTransitionIsRemovedByDelete()
    {
        var vm = Vm(
            new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100)).Transition(V1, "x1", "c1", "c2", 10),
            out var session);
        vm.SelectTransition("x1");
        Assert.Equal("x1", vm.SelectedTransitionId);
        vm.DeleteSelectionCommand.Execute(null);
        Assert.Empty(TimelineQuery.FindTrack(session.Current, V1)!.Transitions);
        Assert.Null(vm.SelectedTransitionId);
    }

    [Fact]
    public void SplitAtPlayheadUsesTheSelectionOrTheTargetedTracks()
    {
        var vm = Vm(Linked(), out var session);
        vm.Playhead = 20;
        vm.Select(["v1"], false);
        vm.SplitAtPlayheadCommand.Execute(null);
        Assert.Equal(2, TimelineQuery.FindTrack(session.Current, V1)!.Clips.Count(c => c.End <= 50));
        Assert.Single(TimelineQuery.FindTrack(session.Current, A1)!.Clips, c => c.End <= 50);

        vm.ClearSelectionCommand.Execute(null);
        vm.Playhead = 120;
        vm.SplitAtPlayheadCommand.Execute(null);
        Assert.Contains(TimelineQuery.FindTrack(session.Current, V1)!.Clips, c => c.Start == 120);
        Assert.Contains(TimelineQuery.FindTrack(session.Current, A1)!.Clips, c => c.Start == 120);
        Assert.Equal(2, session.UndoCount);
    }

    [Fact]
    public void RazorSplitsTheClipAndItsPartners()
    {
        var vm = Vm(Linked(), out var session);
        vm.RazorAt("v1", 30, allTracks: false);
        Assert.Contains(TimelineQuery.FindTrack(session.Current, V1)!.Clips, c => c.Start == 30);
        Assert.Contains(TimelineQuery.FindTrack(session.Current, A1)!.Clips, c => c.Start == 30);
        Assert.Equal("Split", session.UndoLabel);
    }

    [Fact]
    public void InOutAndMarkersAreUndoableSequenceEdits()
    {
        var vm = Vm(Linked(), out var session);
        vm.Playhead = 10;
        vm.SetInCommand.Execute(null);
        vm.Playhead = 39;
        vm.SetOutCommand.Execute(null);
        Assert.Equal(10, vm.InPoint);
        Assert.Equal(40, vm.OutPoint);
        Assert.Equal(40, session.Current.Sequence.OutFrame);
        vm.AddMarkerCommand.Execute(null);
        Assert.Single(session.Current.Sequence.Markers, m => m.Frame == 39);
        vm.GoToInCommand.Execute(null);
        Assert.Equal(10, vm.Playhead);
        vm.NextMarkerCommand.Execute(null);
        Assert.Equal(39, vm.Playhead);
        vm.ClearInOutCommand.Execute(null);
        Assert.Null(vm.InPoint);
        vm.UndoCommand.Execute(null);
        Assert.Equal(10, vm.InPoint);
        vm.InPoint = 5;
        Assert.Equal(5, session.Current.Sequence.InFrame);
    }

    [Fact]
    public void MarkClipUsesTheClipUnderThePlayhead()
    {
        var vm = Vm(Linked(), out _);
        vm.Playhead = 120;
        vm.MarkClipCommand.Execute(null);
        Assert.Equal((100L, 150L), (vm.InPoint!.Value, vm.OutPoint!.Value));
    }

    [Fact]
    public void ZoomCommandsChangeTheViewAndRaiseViewChanged()
    {
        var vm = Vm(Linked(), out _);
        var raised = 0;
        vm.ViewChanged += (_, _) => raised++;
        var before = vm.ViewState.PixelsPerFrame;
        vm.ZoomInCommand.Execute(null);
        Assert.Equal(before * 1.5, vm.ViewState.PixelsPerFrame, 9);
        vm.ZoomOutCommand.Execute(null);
        Assert.Equal(before, vm.ViewState.PixelsPerFrame, 9);
        vm.ZoomToFitCommand.Execute(null);
        Assert.Equal(1000 * 0.95 / 150, vm.ViewState.PixelsPerFrame, 9);
        Assert.Equal(3, raised);
        Assert.InRange(vm.ZoomLevel, 0, 100);
    }

    [Fact]
    public void ZoomSliderIsLogarithmic()
    {
        var vm = Vm(Linked(), out _);
        vm.ZoomLevel = 100;
        Assert.Equal(TimelineViewState.MaxPixelsPerFrame, vm.ViewState.PixelsPerFrame, 6);
        vm.ZoomLevel = 0;
        Assert.Equal(TimelineViewState.MinPixelsPerFrame, vm.ViewState.PixelsPerFrame, 9);
        vm.ZoomLevel = 50;
        Assert.Equal(Math.Sqrt(TimelineViewState.MinPixelsPerFrame * TimelineViewState.MaxPixelsPerFrame), vm.ViewState.PixelsPerFrame, 6);
    }

    [Fact]
    public void ScrollBarPropertiesFollowTheViewState()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("c1", 0, 2000)), out _);
        Assert.Equal(1000, vm.HorizontalViewport);
        vm.ViewState.ScrollToFrame(100);
        Assert.Equal(200, vm.HorizontalOffset, 9);
        vm.HorizontalOffset = 400;
        Assert.Equal(200, vm.ViewState.ScrollFrame, 9);
        Assert.Equal(vm.ViewState.MaxScrollFrame * 2, vm.HorizontalMaximum, 9);
        Assert.False(vm.HasVerticalScroll);
    }

    [Fact]
    public void DropPlacesVideoAndLinkedAudioOnTheMatchingTrack()
    {
        var vm = Vm(Linked(), out var session);
        vm.PlaceMedia(Short, V2, 300, insert: false);
        var v = TimelineQuery.FindTrack(session.Current, V2)!.Clips.Single();
        var a = TimelineQuery.FindTrack(session.Current, A2)!.Clips.Single();
        Assert.Equal((300L, 100L), (v.Start, v.Duration));
        Assert.Equal((300L, 100L), (a.Start, a.Duration));
        Assert.NotNull(v.LinkId);
        Assert.Equal(v.LinkId, a.LinkId);
        Assert.Equal(new[] { a.Id, v.Id }.Order().ToArray(), vm.SelectedClipIds.Order().ToArray());
    }

    [Fact]
    public void DropOnAnAudioTrackPutsTheVideoOnTheMatchingVideoTrack()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("c1", 0, 10)), out var session);
        vm.PlaceMedia(Short, A1, 0, insert: true);
        Assert.Equal([0L, 100L], TimelineQuery.FindTrack(session.Current, V1)!.Clips.Select(c => c.Start).ToArray());
        Assert.Single(TimelineQuery.FindTrack(session.Current, A1)!.Clips);
    }

    [Fact]
    public void SeveralAssetsDropOneAfterAnotherAsOneStep()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("c1", 0, 10)), out var session);
        var before = session.UndoCount;
        vm.PlaceMedia([Short, Still], V1, 20, insert: false);
        var clips = TimelineQuery.FindTrack(session.Current, V1)!.Clips;
        Assert.Equal([0L, 20L, 120L], clips.Select(c => c.Start).ToArray());
        Assert.Equal(125, clips[^1].Duration);
        Assert.Equal(before + 1, session.UndoCount);
        var preview = vm.PreviewPlacements([Short, Still], V1);
        Assert.Equal([0L, 0L, 100L], preview.Select(p => p.Clip.Start).ToArray());
    }

    [Fact]
    public async Task FileDropImportsThenPlacesAtTheDropFrame()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("c1", 0, 10)), out var session);
        await vm.DropFilesAsync(["D:/x.mp4"], V1, 40, insert: false);
        Assert.Single(TimelineQuery.FindTrack(session.Current, V1)!.Clips);

        IReadOnlyList<string>? asked = null;
        vm.ImportFiles = paths =>
        {
            asked = paths;
            return Task.FromResult<IReadOnlyList<string>>([Short, Still]);
        };
        var before = session.UndoCount;
        await vm.DropFilesAsync(["D:/a.mp4", "D:/b.png"], V1, 40, insert: false);
        Assert.Equal(["D:/a.mp4", "D:/b.png"], asked);
        Assert.Equal([0L, 40L, 140L], TimelineQuery.FindTrack(session.Current, V1)!.Clips.Select(c => c.Start).ToArray());
        Assert.Equal(before + 1, session.UndoCount);
    }

    [Fact]
    public void InsertAtPlayheadUsesTheTargetedTracksAndAdvancesThePlayhead()
    {
        var vm = Vm(Linked(), out var session);
        vm.Playhead = 50;
        vm.InsertMediaAtPlayhead(Short);
        Assert.Equal(150, vm.Playhead);
        Assert.Equal(200, TimelineQuery.FindClip(session.Current, "v2")!.Value.Clip.Start);
        Assert.Equal(200, TimelineQuery.FindClip(session.Current, "a2")!.Value.Clip.Start);
        vm.Playhead = 0;
        vm.OverwriteMediaAtPlayhead(Short);
        Assert.Equal(100, vm.Playhead);
        Assert.Equal(100, TimelineQuery.FindTrack(session.Current, V1)!.Clips[0].Duration);
    }

    [Fact]
    public void ToolAndToggleCommands()
    {
        var vm = Vm(Linked(), out _);
        vm.SelectToolCommand.Execute(EditTool.Razor);
        Assert.Equal(EditTool.Razor, vm.Tool);
        vm.ToggleSnapCommand.Execute(null);
        Assert.False(vm.SnapEnabled);
    }

    [Fact]
    public void TrackHeaderTogglesAreUndoable()
    {
        var vm = Vm(Linked(), out var session);
        vm.ToggleTrackLocked(V1);
        Assert.True(TimelineQuery.FindTrack(session.Current, V1)!.Locked);
        vm.ToggleTrackTarget(V2);
        Assert.True(TimelineQuery.FindTrack(session.Current, V2)!.Targeted);
        vm.ToggleTrackMuted(A1);
        vm.ToggleTrackSolo(A1);
        vm.ToggleTrackHidden(V1);
        var a1 = TimelineQuery.FindTrack(session.Current, A1)!;
        Assert.True(a1.Muted && a1.Solo);
        vm.UndoCommand.Execute(null);
        Assert.False(TimelineQuery.FindTrack(session.Current, V1)!.Hidden);
    }

    [Fact]
    public void LockingATrackDropsItsClipsFromTheSelection()
    {
        var vm = Vm(Linked(), out _);
        vm.Select(["v1", "a1"], false);
        vm.ToggleTrackLocked(A1);
        Assert.Equal(["v1"], vm.SelectedClipIds.ToArray());
    }

    [Fact]
    public void NudgeCoalescesIntoOneStep()
    {
        var vm = Vm(Linked(), out var session);
        vm.Select(["v2"], false);
        var before = session.UndoCount;
        vm.NudgeRightCommand.Execute(null);
        vm.NudgeRightCommand.Execute(null);
        Assert.Equal(102, TimelineQuery.FindClip(session.Current, "v2")!.Value.Clip.Start);
        Assert.Equal(before + 1, session.UndoCount);
    }

    [Fact]
    public void RippleTrimToPlayhead()
    {
        var vm = Vm(Linked(), out var session);
        vm.Playhead = 120;
        vm.TrimEndToPlayheadCommand.Execute(null);
        Assert.Equal(20, TimelineQuery.FindClip(session.Current, "v2")!.Value.Clip.Duration);
        Assert.Equal(20, TimelineQuery.FindClip(session.Current, "a2")!.Value.Clip.Duration);
    }

    [Fact]
    public void EscapeDeselectsOrCancelsTheDrag()
    {
        var vm = Vm(Linked(), out _);
        var cancels = 0;
        vm.CancelDragRequested += (_, _) => cancels++;
        vm.Select(["v1"], false);
        vm.IsDragging = true;
        vm.EscapeCommand.Execute(null);
        Assert.Equal(1, cancels);
        Assert.Single(vm.SelectedClipIds);
        vm.IsDragging = false;
        vm.EscapeCommand.Execute(null);
        Assert.Empty(vm.SelectedClipIds);
    }

    [Fact]
    public void WarningsAreRaised()
    {
        var vm = Vm(Linked().Lock(V1), out _);
        var warnings = new List<string>();
        vm.WarningRaised += (_, w) => warnings.Add(w);
        vm.PlaceMedia(Short, V1, 0, insert: false);
        Assert.Contains(warnings, w => w.Contains("locked", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplaceClearsTheSelection()
    {
        var vm = Vm(Linked(), out var session);
        vm.Select(["v1"], false);
        session.Replace(new ProjectBuilder().Build());
        Assert.Empty(vm.SelectedClipIds);
        Assert.Equal(0, vm.SequenceEnd);
    }
}
