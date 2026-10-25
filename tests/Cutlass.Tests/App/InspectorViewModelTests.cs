using System.Globalization;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.App;

public sealed class InspectorViewModelTests
{
    private static InspectorViewModel Vm(ProjectBuilder builder, out TimelineViewModel timeline)
    {
        var session = new ProjectSession(builder.Build(), () => 0);
        timeline = new TimelineViewModel(session, new SequentialIdGenerator(1000));
        timeline.ViewState.SetViewport(1000, 400);
        return new InspectorViewModel(session, timeline);
    }

    private static ProjectBuilder Linked() => new ProjectBuilder()
        .With(V1, V("v1", 0, 50, link: "l1"), V("v2", 50, 50, sourceFrame: 200, link: "l2"))
        .With(A1, A("a1", 0, 50, link: "l1"), A("a2", 50, 50, sourceFrame: 200, link: "l2"));

    private static T Clip<T>(TimelineViewModel timeline, string id)
        where T : Clip => (T)TimelineQuery.FindClip(timeline.Session.Current, id)!.Value.Clip;

    [Fact]
    public void NothingSelectedShowsTheSequence()
    {
        var vm = Vm(Linked(), out _);
        var section = Assert.IsType<SequenceSectionViewModel>(vm.Section);
        Assert.Equal("SEQUENCE", vm.Header);
        Assert.False(section.IsEditable);
        Assert.Equal("1920 x 1080", section.SizeRow.Text);
        Assert.Equal("25 fps", section.RateInfoRow.Text);
        Assert.True(section.ChangeRow.IsVisible);
    }

    [Fact]
    public void SelectionPicksTheSection()
    {
        var builder = Linked().With(V2, Title("t1", 0, 50)).Transition(V1, "x1", "v1", "v2", 10);
        var vm = Vm(builder, out var timeline);

        timeline.Select(["v1"], false);
        Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal("CLIP", vm.Header);

        timeline.Select(["v1", "a1"], false);
        Assert.Equal("2 CLIPS", vm.Header);

        timeline.Select(["t1"], false);
        Assert.IsType<TitleSectionViewModel>(vm.Section);
        Assert.Equal("TITLE", vm.Header);

        timeline.Select(["t1", "v1"], false);
        Assert.IsType<ClipSectionViewModel>(vm.Section);

        timeline.SelectTransition("x1");
        Assert.IsType<TransitionSectionViewModel>(vm.Section);
        Assert.Equal("TRANSITION", vm.Header);

        timeline.Select([], false);
        Assert.IsType<SequenceSectionViewModel>(vm.Section);
    }

    [Fact]
    public void GapSectionClosesTheGap()
    {
        var builder = new ProjectBuilder().With(V1, V("v1", 0, 50), V("v2", 80, 50));
        var vm = Vm(builder, out var timeline);
        timeline.SelectGap(V1, 50, 80);
        var gap = Assert.IsType<GapSectionViewModel>(vm.Section);
        Assert.Equal("GAP", vm.Header);
        Assert.Equal("V1", gap.TrackRow.Text);
        Assert.Equal("00:00:02:00", gap.StartRow.Text);
        Assert.Contains("30 f", gap.LengthRow.Text, StringComparison.Ordinal);

        gap.CloseRow.Command.Execute(null);
        Assert.Equal(50, Clip<MediaClip>(timeline, "v2").Start);
    }

    [Fact]
    public void SectionSurvivesEditsOfTheSameSelection()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = vm.Section;
        var clip = Assert.IsType<ClipSectionViewModel>(section);
        clip.Field("Saturation").Value = 2;
        Assert.Same(section, vm.Section);
        timeline.Session.Undo();
        Assert.Same(section, vm.Section);
        Assert.Equal(1, clip.Field("Saturation").Value);
    }

    [Fact]
    public void SetterRunsUpdateClipsWithTheCoalesceKey()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        var saturation = section.Field("Saturation");
        Assert.Equal("inspector:v1:color.saturation", saturation.CoalesceKey);
        Assert.Equal("inspector:v1:color.saturation", InspectorViewModel.CoalesceKey("v1", "color.saturation"));

        saturation.Value = 2;
        saturation.Value = 2.5;
        Assert.Equal(2.5, Clip<MediaClip>(timeline, "v1").Video.Color.Saturation);
        Assert.Equal(1, timeline.Session.UndoCount);
        Assert.Equal("Saturation", timeline.Session.UndoLabel);

        section.Field("Brightness").Value = 0.25;
        Assert.Equal(2, timeline.Session.UndoCount);
        Assert.Equal(0.25, Clip<MediaClip>(timeline, "v1").Video.Color.Brightness);
    }

    [Fact]
    public void FieldsConvertDisplayUnits()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal(100, section.Field("Scale").Value);
        Assert.Equal(100, section.Field("Opacity").Value);
        section.Field("Scale").Value = 50;
        section.Field("Opacity").Value = 25;
        section.Field("Crop left").Value = 10;
        section.Field("Position X").Value = -120;
        var video = Clip<MediaClip>(timeline, "v1").Video;
        Assert.Equal(0.5, video.Transform.Scale);
        Assert.Equal(0.25, video.Opacity);
        Assert.Equal(0.1, video.Transform.CropLeft, 6);
        Assert.Equal(-120, video.Transform.X);
    }

    [Fact]
    public void EffectSetterRunsUpdateClipsWithTheCoalesceKey()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.True(section.EffectsGroup.IsVisible);
        Assert.True(section.ChromaKeyGroup.IsVisible);
        var rotation = section.Field("Rotation");
        Assert.Equal("inspector:v1:video.effects.rotation", rotation.CoalesceKey);
        Assert.Equal((-180.0, 180.0, 0.5, 1, "°"), (rotation.Minimum, rotation.Maximum, rotation.Step, rotation.Decimals, rotation.Unit));

        rotation.Value = 45;
        rotation.Value = 90;
        Assert.Equal(90, Clip<MediaClip>(timeline, "v1").Video.Effects.RotationDegrees);
        Assert.Equal(1, timeline.Session.UndoCount);
        Assert.Equal("Rotation", timeline.Session.UndoLabel);

        section.Field("Hue").Value = -20;
        section.Field("Blur").Value = 2.5;
        Assert.Equal(3, timeline.Session.UndoCount);
        Assert.Equal(-20, Clip<MediaClip>(timeline, "v1").Video.Effects.HueDegrees);
        Assert.Equal(2.5, Clip<MediaClip>(timeline, "v1").Video.Effects.Blur);
    }

    [Fact]
    public void EffectFieldsConvertDisplayUnits()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal(0, section.Field("Tint").Value);
        Assert.Equal(10, section.Field("Similarity").Value);
        Assert.Equal(6500, section.Field("Temperature").Value);
        section.Field("Tint").Value = 25;
        section.Field("Sharpen").Value = 40;
        section.Field("Vignette").Value = 30;
        section.Field("Similarity").Value = 50;
        section.Field("Blend").Value = 5;
        var effects = Clip<MediaClip>(timeline, "v1").Video.Effects;
        Assert.Equal(0.25, effects.Tint, 6);
        Assert.Equal(0.4, effects.Sharpen, 6);
        Assert.Equal(0.3, effects.Vignette, 6);
        Assert.Equal(0.5, effects.KeySimilarity, 6);
        Assert.Equal(0.05, effects.KeyBlend, 6);
        Assert.Equal(25, section.Field("Tint").Value);
    }

    [Fact]
    public void DifferentToggleValuesShowAsMixed()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Session.Execute(p => TimelineEdits.UpdateClips(p, ["v1"], c => ((MediaClip)c) with { Video = ((MediaClip)c).Video with { Effects = VideoEffects.None with { FlipH = true } } }, "Flip"));
        timeline.Select(["v1", "v2"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Null(section.FlipHRow.IsChecked);
        Assert.False(section.FlipVRow.IsChecked);
        Assert.False(section.GrayscaleRow.IsChecked);
        Assert.Equal("inspector:v1,v2:video.effects.flipH", section.FlipHRow.CoalesceKey);

        section.GrayscaleRow.IsChecked = true;
        Assert.True(Clip<MediaClip>(timeline, "v1").Video.Effects.Grayscale);
        Assert.True(Clip<MediaClip>(timeline, "v2").Video.Effects.Grayscale);
        Assert.Equal("Grayscale", timeline.Session.UndoLabel);

        timeline.Session.Undo();
        Assert.False(section.GrayscaleRow.IsChecked);
    }

    [Fact]
    public void ResetRestoresTheEffectsDefaults()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        section.Field("Hue").Value = 30;
        section.Field("Blur").Value = 4;
        section.FlipVRow.IsChecked = true;
        section.KeyEnabledRow.IsChecked = true;
        Assert.NotEqual(VideoEffects.None, Clip<MediaClip>(timeline, "v1").Video.Effects);

        section.ChromaKeyGroup.ResetCommand.Execute(null);
        Assert.False(Clip<MediaClip>(timeline, "v1").Video.Effects.KeyEnabled);
        Assert.Equal(30, Clip<MediaClip>(timeline, "v1").Video.Effects.HueDegrees);

        section.EffectsGroup.ResetCommand.Execute(null);
        Assert.Equal(VideoEffects.None, Clip<MediaClip>(timeline, "v1").Video.Effects);
        Assert.True(section.EffectsGroup.CanReset);
        Assert.Equal(0, section.Field("Hue").Value);
        Assert.False(section.FlipVRow.IsChecked);
    }

    [Fact]
    public void KeyColorRowEditsTheOpaqueKeyColour()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal("#00FF00FF", section.KeyColorRow.Text);
        Assert.Equal("inspector:v1:video.effects.keyColor", section.KeyColorRow.CoalesceKey);

        section.KeyColorRow.Text = "#f00";
        Assert.Equal("#FF0000", Clip<MediaClip>(timeline, "v1").Video.Effects.KeyColor);
        Assert.Equal("Key Color", timeline.Session.UndoLabel);
        section.KeyEnabledRow.IsChecked = true;
        Assert.True(Clip<MediaClip>(timeline, "v1").Video.Effects.KeyEnabled);

        section.KeyColorRow.Text = "not a colour";
        Assert.Equal("#FF0000", Clip<MediaClip>(timeline, "v1").Video.Effects.KeyColor);
        Assert.Equal("#FF0000FF", section.KeyColorRow.Text);
    }

    [Fact]
    public void AudioEffectSetterRunsUpdateClipsWithTheCoalesceKey()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.True(section.AudioEffectsGroup.IsVisible);
        Assert.False(section.EffectsGroup.IsVisible);
        var highPass = section.Field("High-pass", audio: true);
        Assert.Equal("inspector:a1:audio.effects.highPass", highPass.CoalesceKey);
        Assert.Equal((0.0, 2000.0, 10.0, 0, "Hz"), (highPass.Minimum, highPass.Maximum, highPass.Step, highPass.Decimals, highPass.Unit));

        highPass.Value = 100;
        highPass.Value = 120;
        Assert.Equal(120, Clip<MediaClip>(timeline, "a1").Audio.Effects.HighPassHz);
        Assert.Equal(1, timeline.Session.UndoCount);
        Assert.Equal("High-pass", timeline.Session.UndoLabel);

        section.Field("Bass", audio: true).Value = 3.5;
        section.Field("Gate threshold", audio: true).Value = -50;
        Assert.Equal(3, timeline.Session.UndoCount);
        Assert.Equal(3.5, Clip<MediaClip>(timeline, "a1").Audio.Effects.BassDb);
        Assert.Equal(-50, Clip<MediaClip>(timeline, "a1").Audio.Effects.GateThresholdDb);
        Assert.Equal("Gate Threshold", timeline.Session.UndoLabel);
        // The Audio group keeps the keyframe button as its last row.
        Assert.IsType<InspectorButtonRow>(section.AudioGroup.Rows[^1]);
    }

    [Fact]
    public void PanRowIsPercentOverTheModelRange()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal(0, section.Field("Pan", audio: true).Value);
        Assert.Equal(-20, section.Field("Threshold", audio: true).Value);
        Assert.Equal(4, section.Field("Ratio", audio: true).Value);

        section.Field("Pan", audio: true).Value = -30;
        Assert.Equal(-0.3, Clip<MediaClip>(timeline, "a1").Audio.Effects.Pan, 6);
        Assert.Equal(-30, section.Field("Pan", audio: true).Value);
        Assert.Equal((-100.0, 100.0, "%"), (section.Field("Pan", audio: true).Minimum, section.Field("Pan", audio: true).Maximum, section.Field("Pan", audio: true).Unit));
    }

    [Fact]
    public void DifferentAudioToggleValuesShowAsMixed()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Session.Execute(p => TimelineEdits.UpdateClips(p, ["a1"], c => ((MediaClip)c) with { Audio = ((MediaClip)c).Audio with { Effects = AudioEffects.None with { Mono = true } } }, "Mono"));
        timeline.Select(["a1", "a2"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Null(section.MonoRow.IsChecked);
        Assert.False(section.CompressorRow.IsChecked);
        Assert.False(section.GateRow.IsChecked);
        Assert.Equal("inspector:a1,a2:audio.effects.mono", section.MonoRow.CoalesceKey);

        section.CompressorRow.IsChecked = true;
        Assert.True(Clip<MediaClip>(timeline, "a1").Audio.Effects.Compressor);
        Assert.True(Clip<MediaClip>(timeline, "a2").Audio.Effects.Compressor);
        Assert.Equal("Compressor", timeline.Session.UndoLabel);

        timeline.Session.Undo();
        Assert.False(section.CompressorRow.IsChecked);
    }

    [Fact]
    public void ResetRestoresTheAudioEffectsDefaultsAndKeepsTheGain()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        section.Field("Gain", audio: true).Value = -6;
        section.Field("High-pass", audio: true).Value = 200;
        section.Field("Low-pass", audio: true).Value = 8000;
        section.GateRow.IsChecked = true;
        Assert.NotEqual(AudioEffects.None, Clip<MediaClip>(timeline, "a1").Audio.Effects);

        section.AudioEffectsGroup.ResetCommand.Execute(null);
        Assert.Equal(AudioEffects.None, Clip<MediaClip>(timeline, "a1").Audio.Effects);
        Assert.Equal(-6, Clip<MediaClip>(timeline, "a1").Audio.GainDb);
        Assert.True(section.AudioEffectsGroup.CanReset);
        Assert.Equal(0, section.Field("High-pass", audio: true).Value);
        Assert.False(section.GateRow.IsChecked);
        Assert.Equal("Reset Audio Effects", timeline.Session.UndoLabel);
    }

    [Fact]
    public void AudioGroupResetKeepsTheAudioEffects()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        section.Field("Gain", audio: true).Value = -6;
        section.Field("High-pass", audio: true).Value = 200;

        section.AudioGroup.ResetCommand.Execute(null);

        var audio = Clip<MediaClip>(timeline, "a1").Audio;
        Assert.Equal(0, audio.GainDb);
        Assert.Equal(200, audio.Effects.HighPassHz);
        Assert.Equal("Reset Audio", timeline.Session.UndoLabel);
    }

    [Fact]
    public void LinkedPairAudioEffectsLeaveTheVideoPartnerAlone()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1", "a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.True(section.EffectsGroup.IsVisible);
        Assert.True(section.AudioEffectsGroup.IsVisible);

        section.Field("High-pass", audio: true).Value = 80;
        Assert.Equal(80, Clip<MediaClip>(timeline, "a1").Audio.Effects.HighPassHz);
        Assert.Equal(AudioProps.Default, Clip<MediaClip>(timeline, "v1").Audio);
        Assert.Equal(VideoProps.Default, Clip<MediaClip>(timeline, "v1").Video);
        Assert.Equal("inspector:a1:audio.effects.highPass", section.Field("High-pass", audio: true).CoalesceKey);
    }

    [Fact]
    public void MultiSelectionAppliesToAllAsOneStep()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1", "v2"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        section.Field("Opacity").Value = 50;
        Assert.Equal(0.5, Clip<MediaClip>(timeline, "v1").Video.Opacity);
        Assert.Equal(0.5, Clip<MediaClip>(timeline, "v2").Video.Opacity);
        Assert.Equal(1, timeline.Session.UndoCount);
        Assert.Equal("inspector:v1,v2:video.opacity", section.Field("Opacity").CoalesceKey);
    }

    [Fact]
    public void DifferentValuesShowAsMixed()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Session.Execute(p => TimelineEdits.UpdateClips(p, ["v1"], c => ((MediaClip)c) with { Video = ((MediaClip)c).Video with { Opacity = 0.5 } }, "Opacity"));
        timeline.Select(["v1", "v2"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        var opacity = section.Field("Opacity");
        Assert.True(opacity.IsMixed);
        Assert.True(double.IsNaN(opacity.Value));
        Assert.Equal(100, section.Field("Scale").Value);
        Assert.False(section.Field("Scale").IsMixed);
        Assert.True(section.EnabledRow.IsChecked);
    }

    [Fact]
    public void LinkedPairShowsVideoAndAudioGroups()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1", "a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.True(section.TransformGroup.IsVisible);
        Assert.True(section.AudioGroup.IsVisible);
        Assert.Equal(new[] { "v1" }, section.VideoClipIds.ToArray());
        Assert.Equal(new[] { "a1" }, section.AudioClipIds.ToArray());

        section.Field("Gain", audio: true).Value = -6;
        Assert.Equal(-6, Clip<MediaClip>(timeline, "a1").Audio.GainDb);
        Assert.Equal(0, Clip<MediaClip>(timeline, "v1").Audio.GainDb);
        Assert.Equal("inspector:a1:audio.gain", section.Field("Gain", audio: true).CoalesceKey);
    }

    [Fact]
    public void AudioOnlySelectionHidesTheVideoGroups()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.False(section.TransformGroup.IsVisible);
        Assert.False(section.ColorGroup.IsVisible);
        Assert.True(section.AudioGroup.IsVisible);
    }

    [Fact]
    public void DragIsOneGestureAndEscCancels()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var gamma = Assert.IsType<ClipSectionViewModel>(vm.Section).Field("Gamma");

        gamma.BeginDragCommand.Execute(null);
        gamma.Value = 1.5;
        gamma.Value = 2;
        Assert.True(timeline.Session.InGesture);
        Assert.Equal(2, Clip<MediaClip>(timeline, "v1").Video.Color.Gamma);
        gamma.EndDragCommand.Execute(true);
        Assert.False(timeline.Session.InGesture);
        Assert.Equal(1, timeline.Session.UndoCount);
        Assert.Equal("Gamma", timeline.Session.UndoLabel);

        gamma.BeginDragCommand.Execute(null);
        gamma.Value = 3;
        gamma.EndDragCommand.Execute(false);
        Assert.Equal(2, Clip<MediaClip>(timeline, "v1").Video.Color.Gamma);
        Assert.Equal(2, gamma.Value);
        Assert.Equal(1, timeline.Session.UndoCount);
    }

    [Fact]
    public void ResetRestoresTheGroupDefaults()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        section.Field("Contrast").Value = 1.5;
        section.Field("Gamma").Value = 2;
        section.ColorGroup.ResetCommand.Execute(null);
        Assert.Equal(ColorAdjust.Neutral, Clip<MediaClip>(timeline, "v1").Video.Color);
        Assert.True(section.ColorGroup.CanReset);
    }

    [Fact]
    public void ExpandedStateIsRememberedAcrossSelections()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        Assert.IsType<ClipSectionViewModel>(vm.Section).ColorGroup.IsExpanded = false;
        timeline.Select(["v2"], false);
        Assert.False(Assert.IsType<ClipSectionViewModel>(vm.Section).ColorGroup.IsExpanded);
        Assert.True(Assert.IsType<ClipSectionViewModel>(vm.Section).TransformGroup.IsExpanded);
    }

    [Fact]
    public void TypedStartMovesTheSelection()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("v1", 0, 50)), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal("00:00:00:00", section.StartRow.Text);
        section.StartRow.Text = "00:00:04:00";
        Assert.Equal(100, Clip<MediaClip>(timeline, "v1").Start);
        Assert.Equal("00:00:04:00", section.StartRow.Text);
    }

    [Fact]
    public void TypedDurationTrimsTheEnd()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("v1", 0, 50)), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        section.DurationRow.Text = "30";
        Assert.Equal(30, Clip<MediaClip>(timeline, "v1").Duration);
        Assert.Equal("00:00:01:05", section.DurationRow.Text);
    }

    [Fact]
    public void InvalidTimecodeRestoresTheText()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("v1", 0, 50)), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        section.StartRow.Text = "abc";
        Assert.Equal("00:00:00:00", section.StartRow.Text);
        Assert.Equal(0, timeline.Session.UndoCount);
    }

    [Fact]
    public void SourceInSlipsAndSourceOutTrims()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("v1", 0, 50, sourceFrame: 100)), out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal("00:00:04:00", section.SourceInRow.Text);
        Assert.Equal("00:00:06:00", section.SourceOutRow.Text);

        section.SourceInRow.Text = "00:00:05:00";
        var slipped = Clip<MediaClip>(timeline, "v1");
        Assert.Equal(125 * F, slipped.SourceInFlicks);
        Assert.Equal(0, slipped.Start);
        Assert.Equal(50, slipped.Duration);

        section.SourceOutRow.Text = "00:00:06:00";
        Assert.Equal(25, Clip<MediaClip>(timeline, "v1").Duration);
    }

    [Fact]
    public void FreezeToggleFreezesTheClipAndHidesTheSpeedRow()
    {
        var vm = Vm(Linked().With(V2, V("s1", 0, 50, media: Still)), out var timeline);
        timeline.Select(["v2"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.True(section.FreezeRow.IsVisible);
        Assert.False(section.FreezeRow.IsChecked);
        Assert.True(section.SpeedRow.IsVisible);
        Assert.True(section.SourceInRow.IsVisible);
        Assert.Equal("inspector:v2:clip.freeze", section.FreezeRow.CoalesceKey);

        // The toggle holds the clip's own source in point (frame 200), and the section is reused, so the Speed and
        // Source rows hide in place.
        section.FreezeRow.IsChecked = true;
        Assert.True(Clip<MediaClip>(timeline, "v2").Freeze);
        Assert.Equal(200 * F, Clip<MediaClip>(timeline, "v2").SourceInFlicks);
        Assert.Equal("Freeze Frame", timeline.Session.UndoLabel);
        Assert.Same(section, vm.Section);
        Assert.False(section.SpeedRow.IsVisible);
        Assert.False(section.SourceInRow.IsVisible);
        Assert.False(section.SourceOutRow.IsVisible);

        timeline.Session.Undo();
        Assert.False(section.FreezeRow.IsChecked);
        Assert.True(section.SpeedRow.IsVisible);
        Assert.True(section.SourceInRow.IsVisible);
        Assert.True(section.SourceOutRow.IsVisible);

        // Mixed states show indeterminate; audio-only selections and stills have no freeze row.
        timeline.Session.Execute(p => TimelineEdits.UpdateClips(p, ["v2"], c => ((MediaClip)c) with { Freeze = true }, "Freeze"));
        timeline.Select(["v1", "v2"], false);
        section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Null(section.FreezeRow.IsChecked);
        Assert.True(section.SpeedRow.IsVisible);
        timeline.Select(["a1"], false);
        Assert.False(Assert.IsType<ClipSectionViewModel>(vm.Section).FreezeRow.IsVisible);
        timeline.Select(["s1"], false);
        var still = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.False(still.FreezeRow.IsVisible);
        Assert.False(still.SpeedRow.IsVisible);

        // A mixed selection with a still freezes only the moving clip and keys the gesture on it alone.
        timeline.Select(["v1", "s1"], false);
        var mixed = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Equal("inspector:v1:clip.freeze", mixed.FreezeRow.CoalesceKey);
        Assert.False(mixed.FreezeRow.IsChecked);
        mixed.FreezeRow.IsChecked = true;
        Assert.True(Clip<MediaClip>(timeline, "v1").Freeze);
        Assert.False(Clip<MediaClip>(timeline, "s1").Freeze);
        Assert.True(mixed.FreezeRow.IsChecked);
    }

    [Fact]
    public void StillsHaveNoSourceOrSpeedRows()
    {
        var vm = Vm(new ProjectBuilder().With(V1, V("s1", 0, 50, media: Still)), out var timeline);
        timeline.Select(["s1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.False(section.SourceInRow.IsVisible);
        Assert.False(section.SpeedRow.IsVisible);
        Assert.True(section.TransformGroup.IsVisible);
    }

    [Fact]
    public void EnabledToggleAndShiftE()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1", "a1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.True(section.EnabledRow.IsChecked);
        section.EnabledRow.IsChecked = false;
        Assert.False(Clip<MediaClip>(timeline, "v1").Enabled);
        Assert.False(Clip<MediaClip>(timeline, "a1").Enabled);

        vm.ToggleEnabledCommand.Execute(null);
        Assert.True(Clip<MediaClip>(timeline, "v1").Enabled);
        Assert.True(Clip<MediaClip>(timeline, "a1").Enabled);
    }

    [Fact]
    public void VideoFadesAndAudioKeyframes()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        var video = Assert.IsType<ClipSectionViewModel>(vm.Section);
        video.Field("Fade in").Value = 10;
        Assert.Equal(10, Clip<MediaClip>(timeline, "v1").Video.FadeInFrames);
        Assert.Equal(0, Clip<MediaClip>(timeline, "v1").Audio.FadeInFrames);

        timeline.Select(["a1"], false);
        timeline.Playhead = 20;
        var audio = Assert.IsType<ClipSectionViewModel>(vm.Section);
        Assert.Empty(audio.KeyframeRows);
        audio.AudioGroup.Rows.OfType<InspectorButtonRow>().Single().Command.Execute(null);
        var keys = Clip<MediaClip>(timeline, "a1").Audio.Keys;
        Assert.Equal(new[] { new VolumeKey(20, 0) }, keys.ToArray());
        var row = Assert.Single(audio.KeyframeRows);
        Assert.Equal("00:00:00:20", row.Time.Text);

        row.Level.Value = -12;
        Assert.Equal(-12, Clip<MediaClip>(timeline, "a1").Audio.Keys[0].Db);
        row.Time.Text = "00:00:01:00";
        Assert.Equal(25, Clip<MediaClip>(timeline, "a1").Audio.Keys[0].Frame);
        row.RemoveCommand.Execute(null);
        Assert.Empty(Clip<MediaClip>(timeline, "a1").Audio.Keys);
        Assert.Empty(audio.KeyframeRows);
    }

    [Fact]
    public void LockedClipsReloadTheModelValue()
    {
        var builder = new ProjectBuilder().With(V1, V("v1", 0, 50));
        var vm = Vm(builder, out var timeline);
        timeline.Select(["v1"], false);
        var section = Assert.IsType<ClipSectionViewModel>(vm.Section);
        timeline.Session.Execute(p => TimelineEdits.SetTrack(p, V1, t => t with { Locked = true }, "Lock"));
        section.Field("Saturation").Value = 2;
        Assert.Equal(1, Clip<MediaClip>(timeline, "v1").Video.Color.Saturation);
    }

    [Fact]
    public void TitleFieldsEditTheSpec()
    {
        var vm = Vm(new ProjectBuilder().With(V2, Title("t1", 0, 50), Title("t2", 60, 50)), out var timeline);
        vm.AvailableFonts = ["C:/Windows/Fonts/arial.ttf"];
        timeline.Select(["t1"], false);
        var section = Assert.IsType<TitleSectionViewModel>(vm.Section);
        Assert.Equal("Hello", section.TextRow.Text);
        Assert.Equal("builtin:Inter-Bold", section.FontRow.Selected?.Value);
        Assert.Contains(section.FontRow.Options, o => Equals(o.Value, "C:/Windows/Fonts/arial.ttf") && o.Label == "arial");

        section.TextRow.Text = "Line one\nLine two";
        section.TextRow.Text = "Line one\nLine 2";
        Assert.Equal("Line one\nLine 2", Clip<TitleClip>(timeline, "t1").Title.Text);
        Assert.Equal(1, timeline.Session.UndoCount);

        section.FontRow.Selected = section.FontRow.Options.First(o => Equals(o.Value, "builtin:Inter-Regular"));
        section.Field("Size").Value = 72;
        section.TextColorRow.Text = "#f00";
        section.BoxRow.IsChecked = true;
        section.AnchorRow.Selected = section.AnchorRow.Options.First(o => Equals(o.Value, TitleAnchor.Top));
        section.Field("Offset Y").Value = 40;
        section.Field("Opacity").Value = 80;
        var title = Clip<TitleClip>(timeline, "t1");
        Assert.Equal("builtin:Inter-Regular", title.Title.Font);
        Assert.Equal(72, title.Title.Size);
        Assert.Equal("#FF0000FF", title.Title.Color);
        Assert.True(title.Title.Box);
        Assert.Equal(TitleAnchor.Top, title.Title.Anchor);
        Assert.Equal(40, title.Title.OffsetY);
        Assert.Equal(0.8, title.Opacity, 6);
        Assert.Equal("Hello", Clip<TitleClip>(timeline, "t2").Title.Text);
    }

    [Fact]
    public void SeveralTitlesShareFields()
    {
        var vm = Vm(new ProjectBuilder().With(V2, Title("t1", 0, 50, "A"), Title("t2", 60, 50, "B")), out var timeline);
        timeline.Select(["t1", "t2"], false);
        var section = Assert.IsType<TitleSectionViewModel>(vm.Section);
        Assert.Equal("2 TITLES", vm.Header);
        Assert.Equal(string.Empty, section.TextRow.Text);
        section.Field("Size").Value = 50;
        Assert.Equal(50, Clip<TitleClip>(timeline, "t1").Title.Size);
        Assert.Equal(50, Clip<TitleClip>(timeline, "t2").Title.Size);
        Assert.Equal(1, timeline.Session.UndoCount);
    }

    [Fact]
    public void TransitionSectionEditsKindAndDurationWithClampNotice()
    {
        var builder = new ProjectBuilder()
            .With(V1, V("v1", 0, 50), V("v2", 50, 50, sourceFrame: 200))
            .Transition(V1, "x1", "v1", "v2", 10);
        var vm = Vm(builder, out var timeline);
        timeline.SelectTransition("x1");
        var section = Assert.IsType<TransitionSectionViewModel>(vm.Section);
        Assert.Equal(10, section.DurationRow.Value);
        Assert.Equal("0.40 s", section.SecondsRow.Text);
        Assert.Equal(Enum.GetValues<TransitionKind>().Length - 1, section.KindRow.Options.Count);
        Assert.Equal(TransitionKind.Crossfade, section.KindRow.Selected?.Value);
        Assert.False(section.ClampNotice.IsVisible);

        section.KindRow.Selected = section.KindRow.Options.First(o => Equals(o.Value, TransitionKind.DipToBlack));
        Assert.Equal(TransitionKind.DipToBlack, TimelineQuery.FindTransition(timeline.Session.Current, "x1")!.Value.Transition.Kind);

        section.DurationRow.Value = 20;
        Assert.Equal(20, TimelineQuery.FindTransition(timeline.Session.Current, "x1")!.Value.Transition.Duration);
        Assert.False(section.ClampNotice.IsVisible);

        section.DurationRow.Value = 1000;
        var clamped = TimelineQuery.FindTransition(timeline.Session.Current, "x1")!.Value.Transition.Duration;
        Assert.True(clamped < 1000);
        Assert.Equal(clamped, section.DurationRow.Value);
        Assert.True(section.ClampNotice.IsVisible);
        Assert.StartsWith("Shortened to", section.ClampNotice.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClampNoticeAlsoShowsForATransitionClampedElsewhere()
    {
        // DESIGN 4.4: a transition at its maximum (clamped by Ctrl+D, a drop or a cut edit) shows the red notice too,
        // matching the timeline's red corner flag.
        var project = new ProjectBuilder().With(V1, V("v1", 0, 50), V("v2", 50, 50, sourceFrame: 5)).Build();
        var track = TimelineQuery.FindTrack(project, V1)!;
        var max = TimelineQuery.MaxTransitionDuration(project, track, track.Clips[0], track.Clips[1]);
        var builder = new ProjectBuilder().With(V1, V("v1", 0, 50), V("v2", 50, 50, sourceFrame: 5)).Transition(V1, "x1", "v1", "v2", max);
        var vm = Vm(builder, out var timeline);
        timeline.SelectTransition("x1");
        var section = Assert.IsType<TransitionSectionViewModel>(vm.Section);
        Assert.True(section.ClampNotice.IsVisible);
        Assert.Equal("Shortened to " + max.ToString(CultureInfo.InvariantCulture) + " frames: not enough source before long.mp4", section.ClampNotice.Text);
    }

    [Fact]
    public void AudioTransitionKindIsFixed()
    {
        var builder = new ProjectBuilder()
            .With(A1, A("a1", 0, 50), A("a2", 50, 50, sourceFrame: 200))
            .Transition(A1, "x1", "a1", "a2", 10);
        var vm = Vm(builder, out var timeline);
        timeline.SelectTransition("x1");
        var section = Assert.IsType<TransitionSectionViewModel>(vm.Section);
        Assert.False(section.KindRow.IsEnabled);
        Assert.Equal(TransitionKind.AudioCrossfade, Assert.Single(section.KindRow.Options).Value);
    }

    [Fact]
    public void EmptySequenceIsEditableInPlace()
    {
        var vm = Vm(new ProjectBuilder().Fresh(), out var timeline);
        var section = Assert.IsType<SequenceSectionViewModel>(vm.Section);
        Assert.True(section.IsEditable);
        Assert.True(section.PresetRow.IsVisible);
        Assert.False(section.ChangeRow.IsVisible);

        section.PresetRow.Selected = section.PresetRow.Options.First(o => o.Value is SequenceSizePreset { Width: 1080, Height: 1920 });
        Assert.Equal(new SequenceSettings(1080, 1920, FrameRate.Fps25), timeline.Session.Current.Sequence.Settings);
        section.RateRow.Selected = section.RateRow.Options.First(o => Equals(o.Value, FrameRate.Fps30));
        Assert.Equal(FrameRate.Fps30, timeline.Session.Current.Sequence.Settings.Rate);
        section.WidthRow.Value = 1280;
        Assert.Equal(1280, timeline.Session.Current.Sequence.Settings.Width);
    }

    [Fact]
    public async Task SpeedCommandAppliesTheDialogResult()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1", "a1"], false);
        SpeedDialogViewModel? shown = null;
        vm.ShowSpeedDialog = d =>
        {
            shown = d;
            d.Percent = 50;
            return Task.FromResult(true);
        };
        await vm.OpenSpeedDialogCommand.ExecuteAsync(null);
        Assert.NotNull(shown);
        Assert.Equal(2, shown.ClipCount);
        Assert.Equal(50, Clip<MediaClip>(timeline, "v1").SpeedPercent);
        Assert.Equal(100, Clip<MediaClip>(timeline, "v1").Duration);
        Assert.Equal(100, Clip<MediaClip>(timeline, "a1").Duration);
        Assert.Equal(100, Clip<MediaClip>(timeline, "v2").Start);
        Assert.Equal(1, timeline.Session.UndoCount);
        Assert.Equal("50%", Assert.IsType<ClipSectionViewModel>(vm.Section).SpeedRow.ButtonText);
    }

    [Fact]
    public async Task CancelledSpeedDialogChangesNothing()
    {
        var vm = Vm(Linked(), out var timeline);
        timeline.Select(["v1"], false);
        vm.ShowSpeedDialog = d =>
        {
            d.Percent = 200;
            return Task.FromResult(false);
        };
        await vm.OpenSpeedDialogCommand.ExecuteAsync(null);
        Assert.Equal(0, timeline.Session.UndoCount);
    }

    [Fact]
    public async Task SequenceSettingsCommandAppliesTheDialogResult()
    {
        var vm = Vm(Linked(), out var timeline);
        vm.ShowSequenceSettingsDialog = d =>
        {
            Assert.True(d.HasClips);
            d.SelectedPreset = SequenceSizePreset.All.First(p => p.Width == 1280);
            return Task.FromResult(true);
        };
        await vm.OpenSequenceSettingsCommand.ExecuteAsync(null);
        Assert.Equal(1280, timeline.Session.Current.Sequence.Settings.Width);
        Assert.Equal(720, timeline.Session.Current.Sequence.Settings.Height);
        Assert.Equal("1280 x 720", Assert.IsType<SequenceSectionViewModel>(vm.Section).SizeRow.Text);
    }

    [Fact]
    public void ChainCombinesStepsIntoOneResult()
    {
        var project = Linked().Build();
        var result = InspectorSection.Chain(project, "Duration", ["v1", "a1"], (p, id) => TimelineEdits.Trim(p, id, ClipEdge.End, -10, false, false));
        Assert.Equal("Duration", result.Label);
        Assert.Equal(40, TimelineQuery.FindClip(result.Project, "v1")!.Value.Clip.Duration);
        Assert.Equal(40, TimelineQuery.FindClip(result.Project, "a1")!.Value.Clip.Duration);
        var unchanged = InspectorSection.Chain(project, "None", ["v1"], (_, _) => null);
        Assert.Same(project, unchanged.Project);
    }
}
