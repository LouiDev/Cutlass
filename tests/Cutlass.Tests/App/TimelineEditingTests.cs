using System.Collections.Immutable;
using Cutlass.App.Controls.Timeline;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.App.ViewModels.Viewer;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

/// <summary>M6 timeline view model commands: gaps, Q/W, clipboard, links, tracks, three-point edits, match frame, In/Out.</summary>
public sealed class TimelineEditingTests
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

    private static Clip ClipOf(ProjectSession session, string id) => TimelineQuery.FindClip(session.Current, id)!.Value.Clip;

    private static ImmutableArray<Clip> Clips(ProjectSession session, string trackId) => TimelineQuery.FindTrack(session.Current, trackId)!.Clips;

    [Fact]
    public void GapSelectionExposesItsRangeAndCloseGapClosesIt()
    {
        var vm = Vm(Linked(), out var session);
        var raised = 0;
        vm.SelectionChanged += (_, _) => raised++;
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.Select(["v1"], false);
        vm.SelectGap(V1, 50, 100);
        Assert.Equal(2, raised);
        Assert.Empty(vm.SelectedClipIds);
        Assert.Equal(V1, vm.SelectedGapTrackId);
        Assert.Equal(50, vm.SelectedGapStart);
        Assert.Equal(100, vm.SelectedGapEnd);
        Assert.Contains(nameof(TimelineViewModel.SelectedGapStart), changed);
        vm.CloseGapCommand.Execute(null);
        Assert.Equal(50, ClipOf(session, "v2").Start);
        Assert.Equal(50, ClipOf(session, "a2").Start);
        Assert.Null(vm.SelectedGapTrackId);
        Assert.Equal("Close Gap", session.UndoLabel);
    }

    [Fact]
    public void ATransitionSelectionClearsClipsAndRaisesSelectionChanged()
    {
        var vm = Vm(new ProjectBuilder()
            .With(V1, V("c1", 0, 50), V("c2", 50, 50, sourceFrame: 100))
            .Transition(V1, "x1", "c1", "c2", 10), out _);
        vm.Select(["c1"], false);
        var raised = 0;
        vm.SelectionChanged += (_, _) => raised++;
        vm.SelectTransition("x1");
        Assert.Equal("x1", vm.SelectedTransitionId);
        Assert.Empty(vm.SelectedClipIds);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void QAndWRippleTrimToThePlayhead()
    {
        var vm = Vm(Linked(), out var session);
        vm.Playhead = 120;
        vm.TrimStartToPlayheadCommand.Execute(null);
        var v2 = (MediaClip)ClipOf(session, "v2");
        Assert.Equal(100, v2.Start);
        Assert.Equal(30, v2.Duration);
        Assert.Equal(20 * F, v2.SourceInFlicks);
        Assert.Equal(30, ClipOf(session, "a2").Duration);
        Assert.Equal("Ripple Trim Start", session.UndoLabel);
        vm.Playhead = 110;
        vm.TrimEndToPlayheadCommand.Execute(null);
        Assert.Equal(10, ClipOf(session, "v2").Duration);
        Assert.Equal("Ripple Trim End", session.UndoLabel);
    }

    [Fact]
    public void CopyAndPasteOverwriteAtThePlayheadOnTheSameTracks()
    {
        var vm = Vm(Linked(), out var session);
        Assert.False(vm.HasClipboard);
        vm.PasteCommand.Execute(null);
        Assert.Equal(0, session.UndoCount);
        vm.Select(["v1", "a1"], false);
        vm.CopyCommand.Execute(null);
        Assert.True(vm.HasClipboard);
        vm.Playhead = 150;
        var before = session.UndoCount;
        vm.PasteCommand.Execute(null);
        Assert.Equal(before + 1, session.UndoCount);
        Assert.Equal("Paste", session.UndoLabel);
        var pastedVideo = Clips(session, V1).Single(c => c.Start == 150);
        var pastedAudio = Clips(session, A1).Single(c => c.Start == 150);
        Assert.Equal(50, pastedVideo.Duration);
        Assert.NotNull(pastedVideo.LinkId);
        Assert.Equal(pastedVideo.LinkId, pastedAudio.LinkId);
        Assert.NotEqual("l1", pastedVideo.LinkId);
        Assert.Equal(new[] { pastedAudio.Id, pastedVideo.Id }.Order(), vm.SelectedClipIds.Order());
        Assert.Equal(200, vm.Playhead);
    }

    [Fact]
    public void CutLiftsAndPasteInsertPushesLaterClips()
    {
        var vm = Vm(Linked(), out var session);
        vm.Select(["v2", "a2"], false);
        vm.CutCommand.Execute(null);
        Assert.Equal("Cut", session.UndoLabel);
        Assert.Single(Clips(session, V1));
        vm.Playhead = 0;
        vm.PasteInsertCommand.Execute(null);
        Assert.Equal(50, ClipOf(session, "v1").Start);
        Assert.Equal(50, ClipOf(session, "a1").Start);
        Assert.Equal(0, Clips(session, V1)[0].Start);
        Assert.Equal(50, vm.Playhead);
    }

    [Fact]
    public void LinkUnlinkAndToggle()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("v1", 0, 50)).With(A1, A("a1", 0, 50)), out var session);
        vm.Select(["v1", "a1"], false);
        vm.LinkCommand.Execute(null);
        var link = ClipOf(session, "v1").LinkId;
        Assert.NotNull(link);
        Assert.Equal(link, ClipOf(session, "a1").LinkId);
        vm.ToggleLinkCommand.Execute(null);
        Assert.Null(ClipOf(session, "v1").LinkId);
        Assert.Null(ClipOf(session, "a1").LinkId);
        vm.ToggleLinkCommand.Execute(null);
        Assert.NotNull(ClipOf(session, "a1").LinkId);
        vm.UnlinkCommand.Execute(null);
        Assert.Null(ClipOf(session, "a1").LinkId);
    }

    [Fact]
    public void MoveIntoSyncUsesTheSyncOffset()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("v1", 0, 50, link: "l1")).With(A1, A("a1", 5, 50, link: "l1")), out var session);
        Assert.Equal(-5, TimelineQuery.SyncOffset(session.Current, "a1"));
        vm.Select(["a1"], false);
        vm.MoveIntoSyncCommand.Execute(null);
        Assert.Equal(0, ClipOf(session, "a1").Start);
        Assert.Equal(0, TimelineQuery.SyncOffset(session.Current, "a1"));
        Assert.Equal("Move into Sync", session.UndoLabel);
        vm.UndoCommand.Execute(null);
        vm.MoveClipIntoSync("a1");
        Assert.Equal(0, ClipOf(session, "a1").Start);
    }

    [Fact]
    public void AddRenameAndRemoveTracks()
    {
        var vm = Vm(Linked(), out var session);
        vm.AddVideoTrackCommand.Execute(null);
        var videos = TimelineQuery.TracksOfKind(session.Current, TrackKind.Video);
        Assert.Equal(3, videos.Length);
        Assert.Equal(videos[^1].Id, vm.SelectedTrackId);
        Assert.Equal("V3", videos[^1].Name);
        vm.AddAudioTrackCommand.Execute(null);
        Assert.Equal(3, TimelineQuery.TracksOfKind(session.Current, TrackKind.Audio).Length);
        vm.RenameTrack(V1, "  Main  ");
        Assert.Equal("Main", TimelineQuery.FindTrack(session.Current, V1)!.Name);
        vm.RenameTrack(V1, "   ");
        Assert.Equal("Main", TimelineQuery.FindTrack(session.Current, V1)!.Name);
        vm.AddTrackNextTo(V1, above: true);
        Assert.Equal(1, TimelineQuery.KindIndex(session.Current, vm.SelectedTrackId!));
        vm.AddTrackNextTo(A1, above: false);
        Assert.Equal(1, TimelineQuery.KindIndex(session.Current, vm.SelectedTrackId!));
    }

    [Fact]
    public async Task RemovingANonEmptyTrackAsksFirst()
    {
        var vm = Vm(Linked(), out var session);
        var asked = 0;
        var answer = false;
        vm.ConfirmAsync = (_, _) =>
        {
            asked++;
            return Task.FromResult(answer);
        };
        vm.SelectTrack(V1);
        await vm.RemoveSelectedTrackCommand.ExecuteAsync(null);
        Assert.Equal(1, asked);
        Assert.NotNull(TimelineQuery.FindTrack(session.Current, V1));
        answer = true;
        await vm.RemoveSelectedTrackCommand.ExecuteAsync(null);
        Assert.Null(TimelineQuery.FindTrack(session.Current, V1));
        Assert.Null(vm.SelectedTrackId);
        vm.SelectTrack(V2);
        await vm.RemoveSelectedTrackCommand.ExecuteAsync(null);
        Assert.Equal(2, asked);
        Assert.Single(TimelineQuery.TracksOfKind(session.Current, TrackKind.Video));
    }

    [Fact]
    public void SourceInsertPlacesTheSourceRangeAtThePlayhead()
    {
        var vm = Vm(Linked(), out var session);
        vm.Playhead = 50;
        vm.SourceInsert(Short, 10 * F, 30 * F);
        var placed = Clips(session, V1).Single(c => c.Start == 50);
        Assert.Equal(20, placed.Duration);
        Assert.Equal(10 * F, ((MediaClip)placed).SourceInFlicks);
        Assert.Equal(120, ClipOf(session, "v2").Start);
        Assert.Equal(120, ClipOf(session, "a2").Start);
        Assert.Equal(70, vm.Playhead);
        Assert.Contains(placed.Id, vm.SelectedClipIds);
    }

    [Fact]
    public void SourceOverwriteWithoutSourceRangeUsesTheTimelineInAndOut()
    {
        var vm = Vm(Linked().InOut(10, 40), out var session);
        vm.Playhead = 120;
        vm.SourceOverwrite(Short, 0, null);
        var placed = Clips(session, V1).Single(c => c.Start == 10);
        Assert.Equal(30, placed.Duration);
        Assert.Equal(Short, ((MediaClip)placed).MediaId);
        Assert.Equal(40, vm.Playhead);
        Assert.Equal(100, ClipOf(session, "v2").Start);
    }

    [Fact]
    public void MatchFrameReportsTheClipUnderThePlayhead()
    {
        var vm = Vm(Linked().With(V2, V("t2", 10, 20, media: Short, sourceFrame: 5)).Target(V2, A1), out _);
        MatchFrameEventArgs? args = null;
        vm.MatchFrameRequested += (_, e) => args = e;
        vm.Playhead = 20;
        vm.MatchFrameCommand.Execute(null);
        Assert.NotNull(args);
        Assert.Equal(Short, args.AssetId);
        Assert.Equal(15 * F, args.SourceFlicks);

        // The topmost targeted track has nothing there: any clip under the playhead (V1 here).
        vm.Playhead = 40;
        vm.MatchFrameCommand.Execute(null);
        Assert.Equal(Long, args.AssetId);
        Assert.Equal(40 * F, args.SourceFlicks);

        // Nothing under the playhead: no event.
        args = null;
        vm.Playhead = 70;
        vm.MatchFrameCommand.Execute(null);
        Assert.Null(args);
    }

    [Fact]
    public void SelectInOutRangeSelectsOverlappingClips()
    {
        var vm = Vm(Linked().InOut(40, 110), out _);
        vm.SelectInOutRangeCommand.Execute(null);
        Assert.Equal(new[] { "a1", "a2", "v1", "v2" }, vm.SelectedClipIds.Order(StringComparer.Ordinal));
        var empty = Vm(Linked(), out _);
        empty.SelectInOutRangeCommand.Execute(null);
        Assert.Empty(empty.SelectedClipIds);
    }

    [Fact]
    public void RippleDeleteInOutRangeRemovesAndClosesTheRange()
    {
        var vm = Vm(Linked().InOut(40, 110), out var session);
        var before = session.UndoCount;
        vm.RippleDeleteInOutRangeCommand.Execute(null);
        Assert.Equal(before + 1, session.UndoCount);
        Assert.Equal("Ripple Delete Range", session.UndoLabel);
        Assert.Equal(new[] { (0L, 40L), (40L, 80L) }, Clips(session, V1).Select(c => (c.Start, c.End)));
        Assert.Equal(new[] { (0L, 40L), (40L, 80L) }, Clips(session, A1).Select(c => (c.Start, c.End)));
        Assert.Equal(10 * F, ((MediaClip)Clips(session, V1)[1]).SourceInFlicks);
        Assert.Null(session.Current.Sequence.InFrame);
        Assert.Null(session.Current.Sequence.OutFrame);
        Assert.Empty(TimelineValidator.Check(session.Current));
        vm.UndoCommand.Execute(null);
        Assert.Equal(40, session.Current.Sequence.InFrame);
    }

    [Fact]
    public void VolumeKeysAreAddedSelectedAndDeleted()
    {
        var vm = Vm(Linked(), out var session);
        vm.Select(["a1"], false);
        vm.AddVolumeKey("a1", 10);
        Assert.Equal(new VolumeKeyRef("a1", 10), vm.SelectedVolumeKey);
        Assert.Equal(new[] { new VolumeKey(10, 0) }, ((MediaClip)ClipOf(session, "a1")).Audio.Keys);
        vm.AddVolumeKey("a1", 30);
        Assert.Equal(2, ((MediaClip)ClipOf(session, "a1")).Audio.Keys.Length);
        vm.DeleteSelectionCommand.Execute(null);
        Assert.Equal(new[] { new VolumeKey(10, 0) }, ((MediaClip)ClipOf(session, "a1")).Audio.Keys);
        Assert.Null(vm.SelectedVolumeKey);
        Assert.Contains("a1", vm.SelectedClipIds);
        vm.RemoveVolumeKey("a1", 10);
        Assert.Empty(((MediaClip)ClipOf(session, "a1")).Audio.Keys);
    }

    [Fact]
    public void DeselectingTheClipDropsItsKeySelection()
    {
        var vm = Vm(Linked(), out _);
        vm.Select(["a1"], false);
        vm.AddVolumeKey("a1", 10);
        vm.Select(["v1"], false);
        Assert.Null(vm.SelectedVolumeKey);
    }

    [Fact]
    public void MarkersAreEditedAndRemoved()
    {
        var vm = Vm(Linked().Marker("k1", 20), out var session);
        string? requested = null;
        vm.MarkerEditRequested += (_, id) => requested = id;
        vm.RequestMarkerEdit("k1");
        Assert.Equal("k1", requested);
        vm.UpdateMarker("k1", " Intro ", "#30A46C");
        var marker = session.Current.Sequence.Markers.Single();
        Assert.Equal("Intro", marker.Name);
        Assert.Equal("#30A46C", marker.Color);
        vm.UpdateMarker("k1", "Intro", "nonsense");
        Assert.Equal("#30A46C", session.Current.Sequence.Markers.Single().Color);
        vm.RemoveMarker("k1");
        Assert.Empty(session.Current.Sequence.Markers);
    }

    [Fact]
    public void RenameRequestNamesTheTrack()
    {
        var vm = Vm(Linked(), out _);
        string? requested = null;
        vm.TrackRenameRequested += (_, id) => requested = id;
        vm.RequestTrackRename("nope");
        Assert.Null(requested);
        vm.RequestTrackRename(A2);
        Assert.Equal(A2, requested);
    }
}
