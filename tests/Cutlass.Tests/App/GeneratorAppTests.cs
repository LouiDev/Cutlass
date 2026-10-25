using Cutlass.App.Controls;
using Cutlass.App.Controls.Drag;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

/// <summary>Generator clips (effects batch 5) in the App: the Inspector section, the timeline fades and badges.</summary>
public sealed class GeneratorAppTests
{
    private static InspectorViewModel Vm(ProjectBuilder builder, out TimelineViewModel timeline)
    {
        var session = new ProjectSession(builder.Build(), () => 0);
        timeline = new TimelineViewModel(session, new SequentialIdGenerator(1000));
        timeline.ViewState.SetViewport(1000, 400);
        return new InspectorViewModel(session, timeline);
    }

    private static ProjectBuilder Sample() => new ProjectBuilder()
        .With(V1, V("v1", 0, 50), Generator("g1", 50, 50, color: "#336699"))
        .With(V2, Generator("g2", 0, 50, GeneratorKind.Bars), Title("t1", 60, 20))
        .With(A1, Generator("g3", 0, 50, GeneratorKind.Tone, toneHz: 440));

    private static GeneratorClip G(TimelineViewModel timeline, string id) =>
        Assert.IsType<GeneratorClip>(TimelineQuery.FindClip(timeline.Session.Current, id)!.Value.Clip);

    [Fact]
    public void AllGeneratorSelectionsGetTheGeneratorSectionAndMixedOnesTheClipSection()
    {
        var vm = Vm(Sample(), out var timeline);
        timeline.Select(["g1"], false);
        var section = Assert.IsType<GeneratorSectionViewModel>(vm.Section);
        Assert.Equal(("GENERATOR", "generator:g1", "Color Matte"), (vm.Header, section.Key, section.NameRow.Text));

        timeline.Select(["g1", "g3"], false);
        Assert.Equal("2 GENERATORS", Assert.IsType<GeneratorSectionViewModel>(vm.Section).Header);

        // A media clip or a title in the selection keeps the clip section; its property groups act on media clips only.
        timeline.Select(["g1", "v1"], false);
        var mixed = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal(["v1"], mixed.VideoClipIds.ToArray());
        mixed.Field("Opacity").Value = 40;
        Assert.Equal(0.4, ((MediaClip)TimelineQuery.FindClip(timeline.Session.Current, "v1")!.Value.Clip).Video.Opacity, 6);
        Assert.Equal(1.0, G(timeline, "g1").Video.Opacity);
        timeline.Select(["g2", "t1"], false);
        Assert.IsType<ClipSectionViewModel>(vm.Section);
    }

    [Fact]
    public void AMatteShowsItsColourAndTheVideoGroups()
    {
        var vm = Vm(Sample(), out var timeline);
        timeline.Select(["g1"], false);
        var section = Assert.IsType<GeneratorSectionViewModel>(vm.Section);
        Assert.Equal(["CLIP", "GENERATOR", "TRANSFORM", "COLOR", "EFFECTS", "CHROMA KEY", "FADES", "AUDIO", "AUDIO EFFECTS"], section.Groups.Select(g => g.Title));
        Assert.True(section.ColorRow.IsVisible);
        Assert.False(section.FrequencyRow.IsVisible);
        Assert.True(section.TransformGroup.IsVisible && section.EffectsGroup.IsVisible && section.FadesGroup.IsVisible);
        Assert.False(section.AudioGroup.IsVisible || section.AudioEffectsGroup.IsVisible);
        Assert.Equal("#336699FF", section.ColorRow.Text);
        Assert.Equal("inspector:g1:generator.color", section.ColorRow.CoalesceKey);

        section.ColorRow.Text = "#ff8000";
        Assert.Equal("#FF8000", G(timeline, "g1").Color);
        Assert.Equal("Matte Color", timeline.Session.UndoLabel);

        section.Field("Opacity").Value = 25;
        section.Field("Fade in").Value = 10;
        section.GrayscaleRow.IsChecked = true;
        var matte = G(timeline, "g1");
        Assert.Equal((0.25, 10L, true), (matte.Video.Opacity, matte.Video.FadeInFrames, matte.Video.Effects.Grayscale));
        Assert.Equal("inspector:g1:video.opacity", section.Field("Opacity").CoalesceKey);

        section.GeneratorGroup.ResetCommand.Execute(null);
        Assert.Equal(GeneratorClip.DefaultColor, G(timeline, "g1").Color);
        Assert.Equal("#000000FF", section.ColorRow.Text);
        ModelAssert.Valid(timeline.Session.Current);
    }

    [Fact]
    public void AToneShowsItsFrequencyAndTheAudioGroups()
    {
        var vm = Vm(Sample(), out var timeline);
        timeline.Select(["g3"], false);
        timeline.Playhead = 10;
        var section = Assert.IsType<GeneratorSectionViewModel>(vm.Section);
        Assert.False(section.ColorRow.IsVisible);
        Assert.True(section.FrequencyRow.IsVisible);
        Assert.False(section.TransformGroup.IsVisible || section.FadesGroup.IsVisible);
        Assert.True(section.AudioGroup.IsVisible && section.AudioEffectsGroup.IsVisible);
        Assert.Equal(440, section.FrequencyRow.Value);

        section.FrequencyRow.Value = 880;
        Assert.Equal((880.0, "Tone Frequency"), (G(timeline, "g3").ToneHz, timeline.Session.UndoLabel));
        section.FrequencyRow.Value = 50_000;
        Assert.Equal(GeneratorClip.MaxToneHz, G(timeline, "g3").ToneHz);

        section.Field("Gain", audio: true).Value = -9;
        section.MonoRow.IsChecked = true;
        Assert.Equal((-9.0, true), (G(timeline, "g3").Audio.GainDb, G(timeline, "g3").Audio.Effects.Mono));

        // Tones carry volume keyframes like audio clips.
        section.AudioGroup.Rows.OfType<InspectorButtonRow>().Single().Command.Execute(null);
        Assert.Equal([new VolumeKey(10, 0)], G(timeline, "g3").Audio.Keys.ToArray());
        Assert.Single(section.KeyframeRows);
        ModelAssert.Valid(timeline.Session.Current);
    }

    [Fact]
    public void AKindChangeInPlaceRecomputesTheColourRow()
    {
        // The section is reused while the selection stays; a matte that becomes bars (or back) through the agent
        // shows or hides its colour row on the next refresh.
        var vm = Vm(Sample(), out var timeline);
        timeline.Select(["g2"], false);
        var section = Assert.IsType<GeneratorSectionViewModel>(vm.Section);
        Assert.False(section.ColorRow.IsVisible);
        Assert.False(section.GeneratorGroup.IsVisible);

        timeline.Run(p => TimelineEdits.UpdateClips(p, ["g2"], c => ((GeneratorClip)c) with { Kind = GeneratorKind.ColorMatte }, "Kind"));
        Assert.Same(section, vm.Section);
        Assert.True(section.ColorRow.IsVisible);
        Assert.True(section.GeneratorGroup.IsVisible);
        Assert.Equal("#000000FF", section.ColorRow.Text);

        timeline.Session.Undo();
        Assert.False(section.ColorRow.IsVisible);
    }

    [Fact]
    public void TimelineFadesAndBadgesFollowTheGeneratorSide()
    {
        var matte = Generator("g1", 0, 50) with { Video = VideoProps.Default with { FadeInFrames = 5, FadeOutFrames = 7 } };
        var tone = Generator("g3", 0, 50, GeneratorKind.Tone) with { Audio = AudioProps.Default with { FadeInFrames = 3, FadeOutFrames = 4 } };
        Assert.Equal((5L, 7L), HitTester.Fades(matte));
        Assert.Equal((3L, 4L), HitTester.Fades(tone));
        Assert.Equal(9, ((GeneratorClip)FadeHandleDrag.WithFade(matte, true, 9)).Video.FadeInFrames);
        Assert.Equal(11, ((GeneratorClip)FadeHandleDrag.WithFade(tone, false, 11)).Audio.FadeOutFrames);
        Assert.Equal(0, ((GeneratorClip)FadeHandleDrag.WithFade(tone, false, 11)).Video.FadeOutFrames);

        var p = new ProjectBuilder().With(V1, matte).With(A1, tone).Build();
        Assert.Empty(TimelineControl.Badges(p, matte, offline: false));
        Assert.Equal([("fx", false)], TimelineControl.Badges(p, matte with { Video = matte.Video with { Opacity = 0.5 } }, offline: false));
        Assert.Equal([("fx", false)], TimelineControl.Badges(p, tone with { Audio = tone.Audio with { Effects = AudioEffects.None with { Pan = 0.5 } } }, offline: false));
        Assert.Empty(TimelineControl.Badges(p, tone with { Audio = tone.Audio with { GainDb = -6 } }, offline: false));
        Assert.Equal(Avalonia.Media.Color.Parse("#8A7F45"), TimelinePalette.Default.GeneratorClip);
    }

    [Fact]
    public void AToneHasAnEditableVolumeLineAndKeyframesLikeAudioMedia()
    {
        // A tone on A1 with one key at clip frame 10 (0 dB), a matte on V1 at the same frames.
        var tone = Generator("g3", 0, 50, GeneratorKind.Tone) with { Audio = AudioProps.Default with { Keys = [new VolumeKey(10, 0)] } };
        var session = new ProjectSession(new ProjectBuilder().With(V1, Generator("g1", 0, 50)).With(A1, tone).Build(), () => 0);
        var timeline = new TimelineViewModel(session, new SequentialIdGenerator(1000));
        timeline.ViewState.SetViewport(1000, 400);
        var project = session.Current;
        var a1 = TimelineQuery.FindTrack(project, A1)!;
        var v1 = TimelineQuery.FindTrack(project, V1)!;

        Assert.True(HitTester.HasEditableVolume(a1, tone, new HashSet<string> { "g3" }));
        Assert.False(HitTester.HasEditableVolume(a1, tone, new HashSet<string>()));
        Assert.False(HitTester.HasEditableVolume(v1, v1.Clips[0], new HashSet<string> { "g1" }));
        Assert.Equal(0, VolumeScale.LevelAt(tone, 30), 9);
        Assert.Equal(new[] { 0 }, VolumeScale.SegmentKeys(tone, 30));

        // The dot and the line hit like an audio clip's (TimelineHitZoneTests geometry: 48 px row, 46 px clip).
        double X(double frame) => TimelineViewState.HeaderWidth + timeline.ViewState.FrameToX(frame);
        var layout = timeline.GetLayout();
        var lineY = timeline.ViewState.ToControlY(layout.Row(A1)!.Top) + 1 + VolumeScale.DbToY(0, 0, 46);
        var selected = new HashSet<string> { "g3" };
        var key = HitTester.Hit(project, timeline.ViewState, layout, X(10.5), lineY, 1000, selected);
        Assert.Equal((HitZone.Keyframe, "g3", 0), (key.Zone, key.ClipId, key.KeyIndex));
        Assert.Equal(HitZone.VolumeLine, HitTester.Hit(project, timeline.ViewState, layout, X(30), lineY + 3, 1000, selected).Zone);

        // Ctrl+click adds a key on the line, the line drag and the key drag edit the tone's Audio props.
        timeline.Select(["g3"], false);
        timeline.AddVolumeKey("g3", 30);
        Assert.Equal(new VolumeKeyRef("g3", 30), timeline.SelectedVolumeKey);
        Assert.Equal(new[] { new VolumeKey(10, 0), new VolumeKey(30, 0) }, G(timeline, "g3").Audio.Keys);

        var line = new VolumeLineDrag("g3", 20);
        var press = HitTester.Hit(session.Current, timeline.ViewState, layout, X(20), lineY, 1000, timeline.SelectedClipIds);
        var c = new DragContext(timeline, press, X(20), lineY, false, false, false);
        line.Begin(c);
        c.MoveTo(X(20), lineY + (-6 * 46 / 72.0));
        line.Update(c);
        line.Commit();
        Assert.Equal(new[] { 0, 1 }, line.SegmentKeys);
        Assert.Equal(new[] { new VolumeKey(10, 6), new VolumeKey(30, 6) }, G(timeline, "g3").Audio.Keys);
        Assert.Equal("Volume", session.UndoLabel);

        var drag = new KeyframeDrag("g3", 1);
        var keyPress = HitTester.Hit(session.Current, timeline.ViewState, layout, X(30.5), lineY, 1000, timeline.SelectedClipIds);
        var k = new DragContext(timeline, keyPress, X(30.5), lineY, false, false, false);
        drag.Begin(k);
        k.MoveTo(X(40.5), lineY);
        drag.Update(k);
        drag.Commit();
        Assert.Equal(new VolumeKey(40, 6), G(timeline, "g3").Audio.Keys[1]);
        Assert.Equal("Move Keyframe", session.UndoLabel);
        ModelAssert.Valid(session.Current);

        // A matte has no volume line: adding a key does nothing.
        timeline.AddVolumeKey("g1", 10);
        Assert.Empty(G(timeline, "g1").Audio.Keys);
    }
}
