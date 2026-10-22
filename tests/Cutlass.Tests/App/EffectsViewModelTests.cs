using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

public sealed class EffectsViewModelTests
{
    private static EffectsViewModel Vm(ProjectBuilder builder, out TimelineViewModel timeline)
    {
        var session = new ProjectSession(builder.Build(), () => 0);
        timeline = new TimelineViewModel(session, new SequentialIdGenerator(1000));
        timeline.ViewState.SetViewport(1000, 400);
        return new EffectsViewModel(timeline);
    }

    // Three butting video clips with handles on both sides, and their audio partners.
    private static ProjectBuilder Cuts() => new ProjectBuilder()
        .With(V1, V("v1", 0, 50, sourceFrame: 100), V("v2", 50, 50, sourceFrame: 300), V("v3", 100, 50, sourceFrame: 500))
        .With(A1, A("a1", 0, 50, sourceFrame: 100), A("a2", 50, 50, sourceFrame: 300), A("a3", 100, 50, sourceFrame: 500));

    private static IReadOnlyList<Transition> TransitionsOn(TimelineViewModel timeline, string trackId) =>
        TimelineQuery.FindTrack(timeline.Session.Current, trackId)!.Transitions;

    [Fact]
    public void ListsEveryTransitionTitleAndFade()
    {
        var vm = Vm(Cuts(), out _);
        Assert.Equal(Enum.GetValues<TransitionKind>().Length, vm.Transitions.Count);
        Assert.Equal(["Centered", "Lower Third", "Top Caption"], vm.Titles.Select(t => t.Name));
        Assert.Equal(4, vm.Adjustments.Count);
        Assert.All(vm.Transitions, t => Assert.Equal("1 s", t.Detail));
        Assert.True(vm.Transitions.Single(t => t.TransitionKind == TransitionKind.Crossfade).IsDefault);
        Assert.Equal("cutlass/effect-id", EffectItem.DragFormat);
        Assert.Equal("transition:DipToBlack", vm.Transitions.Single(t => t.TransitionKind == TransitionKind.DipToBlack).Id);
        Assert.Same(vm.Titles[1], vm.Find("title:LowerThird"));
    }

    [Fact]
    public void FramesForSeconds()
    {
        Assert.Equal(25, EffectsViewModel.FramesFor(1, FrameRate.Fps25));
        Assert.Equal(30, EffectsViewModel.FramesFor(1, FrameRate.Fps2997));
        Assert.Equal(120, EffectsViewModel.FramesFor(5, FrameRate.Fps23976));
    }

    [Fact]
    public void TransitionGoesBetweenAdjacentSelectedClips()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Select(["v1", "v2"], false);
        var dip = vm.Transitions.Single(t => t.TransitionKind == TransitionKind.DipToBlack);
        vm.ApplyItemCommand.Execute(dip);
        var transition = Assert.Single(TransitionsOn(timeline, V1));
        Assert.Equal(("v1", "v2"), (transition.LeftClipId, transition.RightClipId));
        Assert.Equal(TransitionKind.DipToBlack, transition.Kind);
        Assert.Equal(25, transition.Duration);
        Assert.Empty(TransitionsOn(timeline, A1));
        Assert.Equal(transition.Id, timeline.SelectedTransitionId);
    }

    [Fact]
    public void SingleClipGetsBothCuts()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Select(["v2"], false);
        vm.AddDefaultTransitionCommand.Execute(null);
        Assert.Equal(2, TransitionsOn(timeline, V1).Count);
        Assert.Equal(1, timeline.Session.UndoCount);
        Assert.Equal("Added 2 transitions.", vm.StatusText);
    }

    [Fact]
    public void NothingSelectedUsesTheCutNearestThePlayheadOnTargetedTracks()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Playhead = 90;
        vm.AddDefaultTransitionCommand.Execute(null);
        var transition = Assert.Single(TransitionsOn(timeline, V1));
        Assert.Equal("v3", transition.RightClipId);
        Assert.Equal(TransitionKind.Crossfade, transition.Kind);
    }

    [Fact]
    public void AudioCrossfadeUsesAudioTracks()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Select(["a1", "a2"], false);
        vm.AddAudioCrossfadeCommand.Execute(null);
        var transition = Assert.Single(TransitionsOn(timeline, A1));
        Assert.Equal(TransitionKind.AudioCrossfade, transition.Kind);
        Assert.Empty(TransitionsOn(timeline, V1));
    }

    [Fact]
    public void SelectedTransitionChangesKind()
    {
        var vm = Vm(Cuts().Transition(V1, "x1", "v1", "v2", 10), out var timeline);
        timeline.SelectTransition("x1");
        vm.Apply(vm.Transitions.Single(t => t.TransitionKind == TransitionKind.WipeLeft));
        var transition = Assert.Single(TransitionsOn(timeline, V1));
        Assert.Equal(TransitionKind.WipeLeft, transition.Kind);
        Assert.Equal(10, transition.Duration);
    }

    [Fact]
    public void NoCutReportsWhy()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("v1", 0, 50)), out var timeline);
        timeline.Select(["v1"], false);
        Assert.Null(vm.Apply(vm.Transitions[0]));
        Assert.Equal("No video cut to add a transition to.", vm.StatusText);
        Assert.Equal(0, timeline.Session.UndoCount);
    }

    [Fact]
    public void DoubleClickOnAVideoTransitionSetsTheDefault()
    {
        var vm = Vm(Cuts(), out var timeline);
        var raised = 0;
        vm.DefaultTransitionChanged += (_, _) => raised++;
        var circle = vm.Transitions.Single(t => t.TransitionKind == TransitionKind.CircleOpen);
        vm.ActivateCommand.Execute(circle);
        Assert.Equal(TransitionKind.CircleOpen, vm.DefaultTransitionKind);
        Assert.True(circle.IsDefault);
        Assert.False(vm.Transitions[0].IsDefault);
        Assert.Equal(1, raised);
        Assert.Equal(0, timeline.Session.UndoCount);

        timeline.Select(["v1", "v2"], false);
        vm.AddDefaultTransitionCommand.Execute(null);
        Assert.Equal(TransitionKind.CircleOpen, Assert.Single(TransitionsOn(timeline, V1)).Kind);
    }

    [Fact]
    public void TitleGoesAboveTheClipsUnderThePlayhead()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Playhead = 20;
        vm.ActivateCommand.Execute(vm.Titles.Single(t => t.Name == "Centered"));
        var track = TimelineQuery.FindTrack(timeline.Session.Current, V2)!;
        var title = Assert.IsType<TitleClip>(Assert.Single(track.Clips));
        Assert.Equal(20, title.Start);
        Assert.Equal(125, title.Duration);
        Assert.Equal(TitleAnchor.Center, title.Title.Anchor);
        Assert.Equal([title.Id], timeline.SelectedClipIds);
        Assert.Equal("Add Title", timeline.Session.UndoLabel);
    }

    [Fact]
    public void TitleAddsATrackWhenNoVideoTrackIsFree()
    {
        var vm = Vm(Cuts().With(V2, Title("t0", 0, 200)), out var timeline);
        timeline.Playhead = 10;
        vm.NewTitleCommand.Execute(null);
        var videos = TimelineQuery.TracksOfKind(timeline.Session.Current, TrackKind.Video);
        Assert.Equal(3, videos.Length);
        var title = Assert.IsType<TitleClip>(Assert.Single(videos[2].Clips));
        Assert.Equal(TitleAnchor.LowerThird, title.Title.Anchor);
        Assert.True(title.Title.Box);
        Assert.Equal(1, timeline.Session.UndoCount);
        timeline.Session.Undo();
        Assert.Equal(2, TimelineQuery.TracksOfKind(timeline.Session.Current, TrackKind.Video).Length);
    }

    [Fact]
    public void FadePresetsSetFadesOnTheSelection()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Select(["v1", "a1"], false);
        vm.Apply(vm.Find("adjust:FadeInOut")!);
        var video = (MediaClip)TimelineQuery.FindClip(timeline.Session.Current, "v1")!.Value.Clip;
        var audio = (MediaClip)TimelineQuery.FindClip(timeline.Session.Current, "a1")!.Value.Clip;
        Assert.Equal(25, video.Video.FadeInFrames);
        Assert.Equal(25, video.Video.FadeOutFrames);
        Assert.Equal(25, audio.Audio.FadeInFrames);
        Assert.Equal(1, timeline.Session.UndoCount);

        vm.Apply(vm.Find("adjust:RemoveFades")!);
        video = (MediaClip)TimelineQuery.FindClip(timeline.Session.Current, "v1")!.Value.Clip;
        Assert.Equal(0, video.Video.FadeInFrames);
        Assert.Equal(0, video.Video.FadeOutFrames);
    }

    [Fact]
    public void FadesNeedASelection()
    {
        var vm = Vm(Cuts(), out var timeline);
        Assert.Null(vm.Apply(vm.Find("adjust:FadeIn")!));
        Assert.Equal("Select clips to fade.", vm.StatusText);
        Assert.Equal(0, timeline.Session.UndoCount);
    }

    [Fact]
    public void ApplyAtSupportsDrops()
    {
        var vm = Vm(Cuts(), out var timeline);
        vm.ApplyAt("transition:SlideLeft", V1, 48);
        var transition = Assert.Single(TransitionsOn(timeline, V1));
        Assert.Equal(("v1", "v2"), (transition.LeftClipId, transition.RightClipId));
        Assert.Equal(TransitionKind.SlideLeft, transition.Kind);

        vm.ApplyAt("title:TopCaption", V2, 30);
        var title = Assert.IsType<TitleClip>(Assert.Single(TimelineQuery.FindTrack(timeline.Session.Current, V2)!.Clips));
        Assert.Equal(30, title.Start);
        Assert.Equal(TitleAnchor.Top, title.Title.Anchor);

        vm.ApplyAt("adjust:FadeIn", V1, 120);
        Assert.Equal(25, ((MediaClip)TimelineQuery.FindClip(timeline.Session.Current, "v3")!.Value.Clip).Video.FadeInFrames);
        Assert.Null(vm.ApplyAt("nope", V1, 0));
    }

    [Fact]
    public void DropPreviewShowsTheCutTheTitleSpanOrTheClip()
    {
        var vm = Vm(Cuts(), out var timeline);
        // The nearest cut on the hovered track, with the 1 s (25 frame) transition centred on it.
        Assert.Equal(new EffectDropPreview(V1, 38, 25, EffectCategory.Transition), vm.PreviewAt("transition:WipeLeft", V1, 48));
        Assert.Equal(new EffectDropPreview(A1, 88, 25, EffectCategory.Transition), vm.PreviewAt("transition:AudioCrossfade", A1, 90));
        Assert.Equal(new EffectDropPreview(V2, 30, 125, EffectCategory.Title), vm.PreviewAt("title:Centered", V2, 30));
        Assert.Equal(new EffectDropPreview(V1, 100, 50, EffectCategory.Adjustment), vm.PreviewAt("adjust:FadeOut", V1, 120));

        // Nothing to do: a video transition on an audio track, an audio crossfade on a video track, a title on audio,
        // a track without cuts, a fade over empty space, an unknown id.
        Assert.Null(vm.PreviewAt("transition:WipeLeft", A1, 48));
        Assert.Null(vm.PreviewAt("transition:AudioCrossfade", V1, 48));
        Assert.Null(vm.PreviewAt("title:Centered", A2, 30));
        Assert.Null(vm.PreviewAt("transition:Crossfade", V2, 30));
        Assert.Null(vm.PreviewAt("adjust:FadeIn", V1, 400));
        Assert.Null(vm.PreviewAt("nope", V1, 0));
        Assert.Equal(0, timeline.Session.UndoCount);
    }

    [Fact]
    public void DropsRefuseTheWrongTrackKindAndLockedTracks()
    {
        var vm = Vm(Cuts().Lock(A1), out var timeline);
        Assert.Null(vm.ApplyAt("transition:DipToWhite", A1, 50));
        Assert.Null(vm.ApplyAt("transition:AudioCrossfade", V1, 50));
        Assert.Equal("Drop Audio Crossfade on an audio cut.", vm.StatusText);
        Assert.Null(vm.PreviewAt("transition:AudioCrossfade", A1, 50));
        Assert.Null(vm.ApplyAt("transition:AudioCrossfade", A1, 50));
        Assert.Equal(0, timeline.Session.UndoCount);
    }

    [Fact]
    public void AFadeDropReachesLinkedPartnersWhileLinkedSelectionIsOn()
    {
        var builder = new ProjectBuilder().With(V1, V("v", 0, 100, link: "l1")).With(A1, A("a", 0, 100, link: "l1"));
        var vm = Vm(builder, out var timeline);
        vm.ApplyAt("adjust:FadeIn", V1, 10);
        Assert.Equal(25, ((MediaClip)TimelineQuery.FindClip(timeline.Session.Current, "a")!.Value.Clip).Audio.FadeInFrames);

        var single = Vm(builder, out var other);
        other.LinkedSelection = false;
        single.ApplyAt("adjust:FadeIn", V1, 10);
        Assert.Equal(0, ((MediaClip)TimelineQuery.FindClip(other.Session.Current, "a")!.Value.Clip).Audio.FadeInFrames);
        Assert.Equal(25, ((MediaClip)TimelineQuery.FindClip(other.Session.Current, "v")!.Value.Clip).Video.FadeInFrames);
    }

    [Theory]
    [InlineData("DipToBlack", TransitionKind.DipToBlack)]
    [InlineData("wipeleft", TransitionKind.WipeLeft)]
    [InlineData("Crossfade", TransitionKind.Crossfade)]
    [InlineData("AudioCrossfade", TransitionKind.Crossfade)]
    [InlineData("Sparkles", TransitionKind.Crossfade)]
    [InlineData("3", TransitionKind.Crossfade)]
    [InlineData("", TransitionKind.Crossfade)]
    [InlineData(null, TransitionKind.Crossfade)]
    public void TheStoredDefaultTransitionParses(string? stored, TransitionKind expected) =>
        Assert.Equal(expected, EffectsViewModel.ParseDefaultTransition(stored));

    [Fact]
    public void SelectionTracksTheSelectedItem()
    {
        var vm = Vm(Cuts(), out _);
        Assert.False(vm.ApplySelectedCommand.CanExecute(null));
        vm.SelectCommand.Execute(vm.Titles[0]);
        Assert.True(vm.Titles[0].IsSelected);
        Assert.True(vm.ApplySelectedCommand.CanExecute(null));
        vm.SelectCommand.Execute(vm.Titles[1]);
        Assert.False(vm.Titles[0].IsSelected);
        Assert.True(vm.Titles[1].IsSelected);
    }
}
