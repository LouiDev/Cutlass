using Cutlass.App.ViewModels;
using Cutlass.App.ViewModels.Timeline;
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

    private static T Clip<T>(TimelineViewModel timeline, string id)
        where T : Clip => (T)TimelineQuery.FindClip(timeline.Session.Current, id)!.Value.Clip;

    [Fact]
    public void ListsEveryTransitionTitleAndFade()
    {
        var vm = Vm(Cuts(), out _);
        Assert.Equal(Enum.GetValues<TransitionKind>().Length, vm.Transitions.Count);
        Assert.Equal(TitleTemplates.Names.Select(TitleTemplates.DisplayName), vm.Titles.Select(t => t.Name));
        Assert.Equal(["Centered", "Lower Third", "Top Caption", "Subtitle", "Headline", "Quote"], vm.Titles.Select(t => t.Name));
        Assert.Equal(12, vm.Adjustments.Count);
        Assert.All(vm.Transitions, t => Assert.Equal("1 s", t.Detail));
        Assert.True(vm.Transitions.Single(t => t.TransitionKind == TransitionKind.Crossfade).IsDefault);
        Assert.Equal("cutlass/effect-id", EffectItem.DragFormat);
        Assert.Equal("transition:DipToBlack", vm.Transitions.Single(t => t.TransitionKind == TransitionKind.DipToBlack).Id);
        Assert.Same(vm.Titles[1], vm.Find("title:LowerThird"));
        Assert.Equal(["Color Matte", "Bars", "Tone"], vm.Generators.Select(g => g.Name));
        Assert.Equal(["generator:ColorMatte", "generator:Bars", "generator:Tone"], vm.Generators.Select(g => g.Id));
        Assert.All(vm.Generators, g => Assert.True(g.IsGenerator && !g.IsTitle && !g.IsTransition && g.Category == EffectCategory.Generator));
        Assert.Same(vm.Generators[2], vm.Find("generator:Tone"));
    }

    [Fact]
    public void GeneratorsGoOnAFreeTrackOfTheirKindAtThePlayhead()
    {
        // Effects batch 5: V1 is busy at the playhead, so the matte goes to V2 above it (the title rule); only A2 is
        // targeted and free, so the tone goes there. The clip lasts the still duration and is selected.
        var vm = Vm(Cuts().Target(V2, A2), out var timeline);
        timeline.StillDurationSeconds = 2;
        Assert.All(vm.Generators, g => Assert.Equal("2 s", g.Detail));
        timeline.Playhead = 10;
        var matte = vm.Apply(vm.Find("generator:ColorMatte")!);
        Assert.NotNull(matte);
        var placed = Clip<GeneratorClip>(timeline, matte.Select[0]);
        Assert.Equal((GeneratorKind.ColorMatte, 10L, 50L, GeneratorClip.DefaultColor), (placed.Kind, placed.Start, placed.Duration, placed.Color));
        Assert.Equal(V2, TimelineQuery.FindClip(timeline.Session.Current, placed.Id)!.Value.Track.Id);
        Assert.Equal([placed.Id], timeline.SelectedClipIds);
        Assert.Equal("Add Color Matte", timeline.Session.UndoLabel);
        Assert.StartsWith("Added Color Matte at ", vm.StatusText, StringComparison.Ordinal);

        var tone = vm.Apply(vm.Find("generator:Tone")!);
        Assert.NotNull(tone);
        Assert.Equal(A2, TimelineQuery.FindClip(timeline.Session.Current, tone.Select[0])!.Value.Track.Id);
        Assert.Equal(GeneratorKind.Tone, Clip<GeneratorClip>(timeline, tone.Select[0]).Kind);
        ModelAssert.Valid(timeline.Session.Current);
    }

    [Fact]
    public void ApplyNeverOverwritesAndAddsATrackWhenEveryTrackIsBusy()
    {
        // V1, V2, A1 and A2 all hold clips over the playhead; V1 and A1 are targeted. Apply leaves them intact and adds
        // V3 for the bars and A3 for the tone (one undo step each, the track and the clip together).
        var builder = Cuts()
            .With(V2, V("busyV", 0, 150, sourceFrame: 700))
            .With(A2, A("busyA", 0, 150, sourceFrame: 700))
            .Target(V1, A1);
        var vm = Vm(builder, out var timeline);
        timeline.Playhead = 20;
        var before = timeline.Session.Current;

        var bars = vm.Apply(vm.Find("generator:Bars")!);
        var tone = vm.Apply(vm.Find("generator:Tone")!);

        var project = timeline.Session.Current;
        Assert.NotNull(bars);
        Assert.NotNull(tone);
        foreach (var id in new[] { V1, V2, A1, A2 })
        {
            Assert.Equal(TimelineQuery.FindTrack(before, id)!.Clips, TimelineQuery.FindTrack(project, id)!.Clips);
        }

        var barsTrack = TimelineQuery.FindClip(project, bars.Select[0])!.Value.Track;
        var toneTrack = TimelineQuery.FindClip(project, tone.Select[0])!.Value.Track;
        Assert.Equal(("V3", TrackKind.Video), (barsTrack.Name, barsTrack.Kind));
        Assert.Equal(("A3", TrackKind.Audio), (toneTrack.Name, toneTrack.Kind));
        Assert.Equal(20, Clip<GeneratorClip>(timeline, tone.Select[0]).Start);
        ModelAssert.Valid(project);

        timeline.Session.Undo();
        Assert.Null(TimelineQuery.FindTrack(timeline.Session.Current, toneTrack.Id));
    }

    [Fact]
    public void ToneApplyPrefersAFreeTargetedTrackThenAnyFreeTrack()
    {
        // A1 is targeted but busy at the playhead, A2 is free: the tone goes to A2 and A1 stays intact.
        var vm = Vm(Cuts().Target(V1, A1), out var timeline);
        timeline.Playhead = 20;
        var tone = vm.Apply(vm.Find("generator:Tone")!);
        Assert.NotNull(tone);
        Assert.Equal(A2, TimelineQuery.FindClip(timeline.Session.Current, tone.Select[0])!.Value.Track.Id);
        Assert.Equal(3, TimelineQuery.FindTrack(timeline.Session.Current, A1)!.Clips.Length);
    }

    [Fact]
    public void GeneratorDropsCheckTheTrackKindAndShowAGhost()
    {
        var vm = Vm(Cuts().Lock(A2), out var timeline);
        var hold = timeline.StillDurationFrames;
        Assert.Equal(new EffectDropPreview(V2, 30, hold, EffectCategory.Generator), vm.PreviewAt("generator:Bars", V2, 30));
        Assert.Equal(new EffectDropPreview(A1, 5, hold, EffectCategory.Generator), vm.PreviewAt("generator:Tone", A1, 5));
        Assert.Null(vm.PreviewAt("generator:Tone", V1, 5));
        Assert.Null(vm.PreviewAt("generator:ColorMatte", A1, 5));
        Assert.Null(vm.PreviewAt("generator:Tone", A2, 5));

        Assert.Null(vm.ApplyAt("generator:Tone", V2, 5));
        Assert.Equal("Drop Tone on an audio track.", vm.StatusText);
        Assert.Null(vm.ApplyAt("generator:Bars", A1, 5));
        Assert.Equal("Drop Bars on a video track.", vm.StatusText);
        Assert.Null(vm.ApplyAt("generator:Tone", A2, 5));
        Assert.Equal("A2 is locked.", vm.StatusText);
        Assert.Equal(0, timeline.Session.UndoCount);

        var bars = vm.ApplyAt("generator:Bars", V2, 30);
        Assert.NotNull(bars);
        var clip = Assert.IsType<GeneratorClip>(Assert.Single(TimelineQuery.FindTrack(timeline.Session.Current, V2)!.Clips));
        Assert.Equal((GeneratorKind.Bars, 30L, hold), (clip.Kind, clip.Start, clip.Duration));
    }

    [Fact]
    public void PresetsReachGeneratorsOfTheirSide()
    {
        var vm = Vm(new ProjectBuilder().With(V1, Generator("g1", 0, 100)).With(A1, Generator("g2", 0, 100, GeneratorKind.Tone)), out var timeline);
        timeline.Select(["g1", "g2"], false);
        vm.Apply(vm.Find("adjust:FadeInOut")!);
        vm.Apply(vm.Find("adjust:Grayscale")!);
        vm.Apply(vm.Find("adjust:Mono")!);
        var matte = Clip<GeneratorClip>(timeline, "g1");
        var tone = Clip<GeneratorClip>(timeline, "g2");
        Assert.Equal((25L, 25L, true, false), (matte.Video.FadeInFrames, matte.Video.FadeOutFrames, matte.Video.Effects.Grayscale, matte.Audio.Effects.Mono));
        Assert.Equal((25L, 25L, true, false), (tone.Audio.FadeInFrames, tone.Audio.FadeOutFrames, tone.Audio.Effects.Mono, tone.Video.Effects.Grayscale));
    }

    [Fact]
    public void EveryTransitionKindHasANameABadgeAndAGroup()
    {
        var vm = Vm(Cuts(), out _);
        var kinds = Enum.GetValues<TransitionKind>();
        foreach (var kind in kinds)
        {
            Assert.NotEqual(kind.ToString(), EffectItem.TransitionName(kind));
            var badge = EffectItem.TransitionBadge(kind);
            Assert.NotEqual("?", badge);
            Assert.InRange(badge.Length, 1, 3);
            Assert.DoesNotContain((char)0x2014, badge);   // no em dash, as everywhere in the repo
        }

        var names = kinds.Select(EffectItem.TransitionName).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());

        // The five groups partition Transitions (the union the drops and Find use).
        var grouped = vm.TransitionFades.Concat(vm.TransitionWipes).Concat(vm.TransitionSlides).Concat(vm.TransitionShapes).Concat(vm.TransitionAudio).ToList();
        Assert.Equal(vm.Transitions.Count, grouped.Count);
        Assert.Equal(vm.Transitions.OrderBy(t => t.Id, StringComparer.Ordinal), grouped.OrderBy(t => t.Id, StringComparer.Ordinal));
        Assert.Same(vm.Find("transition:AudioCrossfade"), Assert.Single(vm.TransitionAudio));
        Assert.Contains(vm.Find("transition:Pixelize"), vm.TransitionFades);
        Assert.Contains(vm.Find("transition:WipeUp"), vm.TransitionWipes);
        Assert.Contains(vm.Find("transition:ZoomIn"), vm.TransitionSlides);
        Assert.Contains(vm.Find("transition:RectCrop"), vm.TransitionShapes);
        Assert.Equal(TransitionGroup.Shapes, EffectItem.TransitionGroup(TransitionKind.VerticalClose));
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
    public void TitleTemplatesComeFromCoreAndScaleWithTheSequence()
    {
        var vm = Vm(Cuts(), out var timeline);
        foreach (var name in TitleTemplates.Names)
        {
            var item = vm.Find("title:" + name);
            Assert.NotNull(item);
            Assert.True(TitleTemplates.TryGet(name, out var spec));
            Assert.Equal(spec, item.Title);
            Assert.Contains(TitleTemplates.Description(name), item.Description, StringComparison.Ordinal);
        }

        // A 1080-line sequence places the template unscaled; a 540-line one halves the sizes (TitleTemplates.Scale).
        timeline.Playhead = 10;
        vm.ApplyAt("title:Quote", V2, 10);
        var quote = Assert.IsType<TitleClip>(Assert.Single(TimelineQuery.FindTrack(timeline.Session.Current, V2)!.Clips));
        Assert.True(TitleTemplates.TryGet("Quote", out var expected));
        Assert.Equal(expected, quote.Title);
        timeline.Session.Execute(p => TimelineEdits.SetSequenceSettings(p, new SequenceSettings(960, 540, FrameRate.Fps25)));
        vm.ApplyAt("title:Headline", V2, 200);
        var headline = Assert.IsType<TitleClip>(TimelineQuery.FindTrack(timeline.Session.Current, V2)!.Clips[^1]);
        Assert.Equal((60.0, 2.0, TitleAnchor.Top), (headline.Title.Size, headline.Title.OutlineWidth, headline.Title.Anchor));
    }

    [Fact]
    public void FreezeFramePresetHoldsTheClipAtThePlayheadAndAsADrop()
    {
        var vm = Vm(Cuts(), out var timeline);
        var hold = timeline.StillDurationFrames;
        Assert.Null(vm.Apply(vm.Find("adjust:FreezeFrame")!));
        Assert.Equal("Select clips to apply the preset to.", vm.StatusText);
        timeline.Select(["v2"], false);
        timeline.Playhead = 20;
        Assert.Null(vm.Apply(vm.Find("adjust:FreezeFrame")!));
        Assert.Equal("Select a video clip under the playhead to freeze.", vm.StatusText);
        Assert.Equal(0, timeline.Session.UndoCount);

        timeline.Select(["v1"], false);
        var result = vm.Apply(vm.Find("adjust:FreezeFrame")!);
        Assert.NotNull(result);
        var frozen = Clip<MediaClip>(timeline, result.Select[0]);
        Assert.Equal((20L, hold, true, 120 * F), (frozen.Start, frozen.Duration, frozen.Freeze, frozen.SourceInFlicks));
        Assert.Equal([frozen.Id], timeline.SelectedClipIds);
        Assert.Equal("Freeze Frame", timeline.Session.UndoLabel);
        Assert.StartsWith("Freeze frame at ", vm.StatusText, StringComparison.Ordinal);
        Assert.Equal(50 + hold, Clip<MediaClip>(timeline, "v2").Start);
        Assert.Equal(50 + hold, Clip<MediaClip>(timeline, "a2").Start);

        // A drop freezes the clip under the pointer at the drop frame: the right half of v1 starts at 20 + hold with source 120.
        var drop = vm.ApplyAt("adjust:FreezeFrame", V1, 20 + hold + 10);
        Assert.NotNull(drop);
        var dropped = Clip<MediaClip>(timeline, drop.Select[0]);
        Assert.Equal((20 + hold + 10, hold, true, 130 * F), (dropped.Start, dropped.Duration, dropped.Freeze, dropped.SourceInFlicks));
        Assert.Equal(new EffectDropPreview(V1, dropped.Start, hold, EffectCategory.Adjustment), vm.PreviewAt("adjust:FreezeFrame", V1, dropped.Start + 1));
        Assert.Null(vm.ApplyAt("adjust:FreezeFrame", A1, 5));
        Assert.Equal(2, timeline.Session.UndoCount);
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
    public void GrayscalePresetTogglesTheSelection()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Select(["v1", "a1"], false);
        vm.Apply(vm.Find("adjust:Grayscale")!);
        Assert.True(Clip<MediaClip>(timeline, "v1").Video.Effects.Grayscale);
        Assert.Equal(VideoProps.Default, Clip<MediaClip>(timeline, "a1").Video);
        Assert.Equal("Grayscale applied.", vm.StatusText);
        Assert.Equal(1, timeline.Session.UndoCount);

        vm.Apply(vm.Find("adjust:Grayscale")!);
        Assert.False(Clip<MediaClip>(timeline, "v1").Video.Effects.Grayscale);
    }

    [Fact]
    public void FlipHorizontalPresetMirrorsAndMirrorsBack()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Select(["v1"], false);
        vm.Apply(vm.Find("adjust:FlipH")!);
        Assert.True(Clip<MediaClip>(timeline, "v1").Video.Effects.FlipH);
        Assert.Equal("Flip Horizontal", timeline.Session.UndoLabel);
        vm.Apply(vm.Find("adjust:FlipH")!);
        Assert.Equal(VideoEffects.None, Clip<MediaClip>(timeline, "v1").Video.Effects);
    }

    [Fact]
    public void FlipVerticalPresetWorksAsADrop()
    {
        var vm = Vm(Cuts(), out var timeline);
        vm.ApplyAt("adjust:FlipV", V1, 10);
        Assert.True(Clip<MediaClip>(timeline, "v1").Video.Effects.FlipV);
        Assert.False(Clip<MediaClip>(timeline, "v1").Video.Effects.FlipH);
        Assert.Equal(new EffectDropPreview(V1, 0, 50, EffectCategory.Adjustment), vm.PreviewAt("adjust:FlipV", V1, 10));
    }

    [Fact]
    public void ResetEffectsPresetRestoresNone()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Session.Execute(p => TimelineEdits.UpdateClips(p, ["v1"], c => ((MediaClip)c) with { Video = ((MediaClip)c).Video with { Effects = VideoEffects.None with { HueDegrees = 30, Blur = 4, KeyEnabled = true } } }, "Effects"));
        timeline.Select(["v1"], false);
        vm.Apply(vm.Find("adjust:ResetEffects")!);
        Assert.Equal(VideoEffects.None, Clip<MediaClip>(timeline, "v1").Video.Effects);
        Assert.Equal("Reset Effects applied.", vm.StatusText);

        var before = timeline.Session.UndoCount;
        vm.Apply(vm.Find("adjust:ResetEffects")!);
        Assert.Equal("Nothing to change.", vm.StatusText);
        Assert.Equal(before, timeline.Session.UndoCount);
    }

    [Fact]
    public void MonoPresetTogglesTheSelectedAudioClips()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Select(["v1", "a1"], false);
        vm.Apply(vm.Find("adjust:Mono")!);
        Assert.True(Clip<MediaClip>(timeline, "a1").Audio.Effects.Mono);
        Assert.Equal(AudioProps.Default, Clip<MediaClip>(timeline, "v1").Audio);
        Assert.Equal("Mono applied.", vm.StatusText);
        Assert.Equal(1, timeline.Session.UndoCount);

        vm.Apply(vm.Find("adjust:Mono")!);
        Assert.Equal(AudioEffects.None, Clip<MediaClip>(timeline, "a1").Audio.Effects);
    }

    [Fact]
    public void VoicePresetSetsAnEightyHertzHighPassAsADrop()
    {
        var vm = Vm(Cuts(), out var timeline);
        vm.ApplyAt("adjust:Voice", A1, 10);
        Assert.Equal(80, Clip<MediaClip>(timeline, "a1").Audio.Effects.HighPassHz);
        Assert.Equal("Voice High-pass", timeline.Session.UndoLabel);
        Assert.Equal(new EffectDropPreview(A1, 0, 50, EffectCategory.Adjustment), vm.PreviewAt("adjust:Voice", A1, 10));

        // Dropped on a video clip it changes nothing (the audio partner is unlinked here).
        var before = timeline.Session.UndoCount;
        vm.ApplyAt("adjust:Voice", V1, 10);
        Assert.Equal(AudioProps.Default, Clip<MediaClip>(timeline, "v1").Audio);
        Assert.Equal(before, timeline.Session.UndoCount);
    }

    [Fact]
    public void ResetAudioEffectsPresetRestoresNone()
    {
        var vm = Vm(Cuts(), out var timeline);
        timeline.Session.Execute(p => TimelineEdits.UpdateClips(p, ["a1"], c => ((MediaClip)c) with { Audio = ((MediaClip)c).Audio with { GainDb = -6, Effects = AudioEffects.None with { HighPassHz = 120, Gate = true, Pan = -0.5 } } }, "Effects"));
        timeline.Select(["a1"], false);
        vm.Apply(vm.Find("adjust:ResetAudioEffects")!);
        Assert.Equal(AudioEffects.None, Clip<MediaClip>(timeline, "a1").Audio.Effects);
        Assert.Equal(-6, Clip<MediaClip>(timeline, "a1").Audio.GainDb);
        Assert.Equal("Reset Audio Effects applied.", vm.StatusText);

        var before = timeline.Session.UndoCount;
        vm.Apply(vm.Find("adjust:ResetAudioEffects")!);
        Assert.Equal("Nothing to change.", vm.StatusText);
        Assert.Equal(before, timeline.Session.UndoCount);
    }

    [Fact]
    public void FadesNeedASelection()
    {
        var vm = Vm(Cuts(), out var timeline);
        Assert.Null(vm.Apply(vm.Find("adjust:FadeIn")!));
        Assert.Equal("Select clips to apply the preset to.", vm.StatusText);
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
