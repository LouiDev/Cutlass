using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

public sealed class PropsTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    [Theory]
    [InlineData(50, 100)]
    [InlineData(200, 25)]
    [InlineData(25, 200)]
    [InlineData(400, 13)]
    [InlineData(150, 33)]
    public void SpeedKeepsTheSourceRange(int percent, long duration)
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 50, 10, sourceFrame: 300)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetSpeed(p, ["c1"], percent, rippleFollowing: true));
        var c1 = ModelAssert.Media(q, "c1");
        Assert.Equal((percent, duration), (c1.SpeedPercent, c1.Duration));
        Assert.Equal(duration, ModelAssert.Clip(q, "c2").Start);
    }

    [Fact]
    public void SpeedWithRippleShiftsOtherTracks()
    {
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 50, sourceFrame: 100, link: "l1"))
            .With(A1, A("a1", 0, 50, sourceFrame: 100, link: "l1"), A("a2", 60, 10, sourceFrame: 300))
            .With(V2, Title("t1", 70, 10))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetSpeed(p, ["c1", "a1"], 50, rippleFollowing: true));
        Assert.Equal(100, ModelAssert.Clip(q, "a1").Duration);
        Assert.Equal(110, ModelAssert.Clip(q, "a2").Start);
        Assert.Equal(120, ModelAssert.Clip(q, "t1").Start);
    }

    [Fact]
    public void SpeedWithoutRippleStopsAtTheNextClip()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, sourceFrame: 100), V("c2", 70, 10, sourceFrame: 300)).Build();
        var r = TimelineEdits.SetSpeed(p, ["c1"], 50, rippleFollowing: false);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(70, ModelAssert.Clip(q, "c1").Duration);
        Assert.Equal(70, ModelAssert.Clip(q, "c2").Start);
        Assert.Contains("Shortened to fit before the next clip.", r.Warnings);
    }

    [Fact]
    public void SpeedClampsToTheRangeAndTheSource()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 40, media: Short, sourceFrame: 60)).Build();
        var r = TimelineEdits.SetSpeed(p, ["c1"], 10, true);
        var c1 = ModelAssert.Media(ModelAssert.Changed(p, r), "c1");
        Assert.Equal(25, c1.SpeedPercent);
        Assert.Equal(160, c1.Duration);   // only 40 source frames remain, which last 160 frames at 25 %
        Assert.Contains($"Speed is limited to {TimelineEdits.MinSpeedPercent} to {TimelineEdits.MaxSpeedPercent} percent.", r.Warnings);
    }

    [Fact]
    public void SpeedScalesKeyframesAndSkipsTitlesAndStills()
    {
        var clip = A("a1", 0, 40, sourceFrame: 100) with { Audio = AudioProps.Default with { Keys = [new VolumeKey(10, -6), new VolumeKey(30, 0)] } };
        var p = new ProjectBuilder().With(A1, clip).With(V1, Title("t1", 0, 10), V("s1", 10, 10, media: Still)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetSpeed(p, ["a1", "t1", "s1"], 200, true));
        Assert.Equal([new VolumeKey(5, -6), new VolumeKey(15, 0)], ModelAssert.Media(q, "a1").Audio.Keys.ToArray());
        Assert.Equal(10, ModelAssert.Clip(q, "t1").Duration);
        var r = TimelineEdits.SetSpeed(p, ["t1"], 200, true);
        ModelAssert.Unchanged(p, r);
        Assert.Contains("Speed applies to video and audio clips only.", r.Warnings);
    }

    [Fact]
    public void FrozenClipsRefuseSpeedAndSlipAndKeepTheirSourceWhenTrimmed()
    {
        // A freeze frame of source frame 100 (effects batch 4): no speed, no slip, and trimming the head keeps the frame.
        var p = new ProjectBuilder().With(V1, V("f1", 10, 50, sourceFrame: 100, freeze: true), V("c2", 60, 20, sourceFrame: 300)).Build();
        var speed = TimelineEdits.SetSpeed(p, ["f1"], 200, true);
        ModelAssert.Unchanged(p, speed);
        Assert.Contains("Frozen clips have no speed.", speed.Warnings);
        var slip = TimelineEdits.Slip(p, "f1", 5, includeLinked: false);
        ModelAssert.Unchanged(p, slip);
        Assert.Contains("Frozen clips have no slip.", slip.Warnings);

        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "f1", ClipEdge.Start, 20, ripple: false, includeLinked: false));
        var f1 = ModelAssert.Media(q, "f1");
        Assert.Equal((30L, 30L, 100 * F, true), (f1.Start, f1.Duration, f1.SourceInFlicks, f1.Freeze));
        Assert.Equal(100 * F, TimeMath.SourceFlicksAt(f1, 55, Rate));

        // The frozen frame is kept by moves and by a sequence rate change, and UpdateClips leaves the flag alone.
        var moved = ModelAssert.Media(ModelAssert.Changed(p, TimelineEdits.Move(_ids, p, ["f1"], 100, 0, EditMode.Overwrite)), "f1");
        Assert.Equal((100 * F, true), (moved.SourceInFlicks, moved.Freeze));
        var rated = ModelAssert.Media(ModelAssert.Changed(p, TimelineEdits.SetSequenceSettings(p, new SequenceSettings(1920, 1080, FrameRate.Fps30))), "f1");
        Assert.Equal((100 * F, true), (rated.SourceInFlicks, rated.Freeze));
        var toggled = ModelAssert.Media(ModelAssert.Changed(p, TimelineEdits.UpdateClips(p, ["c2"], c => ((MediaClip)c) with { Freeze = true }, "Freeze")), "c2");
        Assert.True(toggled.Freeze);
        Assert.Equal(TimelineQuery.Unbounded, TimelineQuery.TailHandle(q, f1));
    }

    [Fact]
    public void UnfreezingAHoldLongerThanItsSourceTrimsOrRemovesIt()
    {
        // short.mov has 100 frames: a 50-frame hold of source frame 95 keeps 5 frames once it plays again.
        var p = new ProjectBuilder().With(V1, V("c1", 0, 10, media: Short, sourceFrame: 90)).Build();
        var frozen = TimelineEdits.FreezeFrame(_ids, p, "c1", 5, 50, ripple: true);
        var q = ModelAssert.Changed(p, frozen);
        var holdId = frozen.Select[0];
        var r = TimelineEdits.UpdateClips(q, [holdId], c => ((MediaClip)c) with { Freeze = false }, "Freeze");
        var s = ModelAssert.Changed(q, r);
        var hold = ModelAssert.Media(s, holdId);
        Assert.Equal((5L, 5L, 95 * F, false), (hold.Start, hold.Duration, hold.SourceInFlicks, hold.Freeze));
        Assert.Contains("Shortened a clip of short.mov to fit its media.", r.Warnings);

        // A hold whose frame lies past the source end (after a relink, say) has nothing left to play.
        var past = new ProjectBuilder().With(V1, V("f1", 0, 20, media: Short, sourceFrame: 120, freeze: true), V("c2", 20, 10)).Build();
        var gone = TimelineEdits.UpdateClips(past, ["f1"], c => ((MediaClip)c) with { Freeze = false }, "Freeze");
        Assert.Equal(["c2"], ModelAssert.Track(ModelAssert.Changed(past, gone), V1).Clips.Select(c => c.Id));
        Assert.Contains("Removed a clip of short.mov: it lies outside the media.", gone.Warnings);
    }

    [Fact]
    public void FreezeIsVideoOnly()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50)).With(A1, A("a1", 0, 50)).Build();
        Assert.False(((MediaClip)TimelineEdits.SanitizeClip(A("a2", 0, 10) with { Freeze = true })).Freeze);
        var r = TimelineEdits.UpdateClips(p, ["c1", "a1"], c => ((MediaClip)c) with { Freeze = true }, "Freeze");
        Assert.True(ModelAssert.Media(r.Project, "c1").Freeze);
        Assert.False(ModelAssert.Media(r.Project, "a1").Freeze);
        ModelAssert.Valid(r.Project);
    }

    [Fact]
    public void SlippingWithLinkedClipsLeavesAFrozenPartnerAlone()
    {
        var p = new ProjectBuilder()
            .With(V1, V("f1", 0, 50, sourceFrame: 100, link: "l1", freeze: true))
            .With(A1, A("a1", 0, 50, sourceFrame: 100, link: "l1"))
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Slip(p, "a1", 5, includeLinked: true));
        Assert.Equal(105 * F, ModelAssert.Media(q, "a1").SourceInFlicks);
        Assert.Equal((100 * F, true), (ModelAssert.Media(q, "f1").SourceInFlicks, ModelAssert.Media(q, "f1").Freeze));
    }

    [Fact]
    public void ReplacingMediaMovesAFrozenFramePastTheNewEndToTheLastFrame()
    {
        var p = new ProjectBuilder().With(V1, V("f1", 0, 80, media: Short, sourceFrame: 90, freeze: true), V("f2", 80, 10, media: Short, sourceFrame: 20, freeze: true)).Build();

        // The re-probed file is 2 s (50 frames): the hold of frame 90 moves to frame 49 and keeps its length.
        var shorter = Asset(Short, "D:/Footage/short.mov", 2_000_000, video: true, audio: true);
        var r = TimelineEdits.ImportMedia(p, [shorter]);
        var q = ModelAssert.Changed(p, r);
        var f1 = ModelAssert.Media(q, "f1");
        Assert.Equal((0L, 80L, 49 * F, true), (f1.Start, f1.Duration, f1.SourceInFlicks, f1.Freeze));
        Assert.Equal(20 * F, ModelAssert.Media(q, "f2").SourceInFlicks);
        Assert.Contains("Moved a frozen clip of short.mov to the last frame of its media.", r.Warnings);

        // A file without video takes its frozen clips with it.
        var audioOnly = Asset(Short, "D:/Footage/short.mov", 2_000_000, video: false, audio: true);
        Assert.Empty(ModelAssert.Track(ModelAssert.Changed(p, TimelineEdits.ImportMedia(p, [audioOnly])), V1).Clips);
    }

    [Fact]
    public void UpdateClipsAppliesToAllAndSanitizes()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50), V("c2", 50, 20, sourceFrame: 300)).Build();
        var r = TimelineEdits.UpdateClips(p, ["c1", "c2"], c => c is MediaClip m
            ? m with { Video = m.Video with { Opacity = 3, FadeInFrames = 30, FadeOutFrames = 30, Transform = m.Video.Transform with { CropLeft = 0.8, CropRight = 0.8 } } }
            : c, "Opacity");
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Opacity", r.Label);
        var c1 = ModelAssert.Media(q, "c1");
        Assert.Equal(1, c1.Video.Opacity);
        Assert.Equal((30L, 20L), (c1.Video.FadeInFrames, c1.Video.FadeOutFrames));
        Assert.Equal(0.8, c1.Video.Transform.CropLeft);
        Assert.Equal(0.1, c1.Video.Transform.CropRight, 9);
        var c2 = ModelAssert.Media(q, "c2");
        Assert.Equal((20L, 0L), (c2.Video.FadeInFrames, c2.Video.FadeOutFrames));
    }

    [Fact]
    public void UpdateClipsProtectsPlacementAndIdentity()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50)).Build();
        var r = TimelineEdits.UpdateClips(p, ["c1"], c => ((MediaClip)c) with { Start = 99, Duration = 3, SourceInFlicks = 5, Enabled = false }, "Edit");
        var c1 = ModelAssert.Media(ModelAssert.Changed(p, r), "c1");
        Assert.Equal((0L, 50L, 0L, false), (c1.Start, c1.Duration, c1.SourceInFlicks, c1.Enabled));
        Assert.NotEmpty(r.Warnings);
        ModelAssert.Unchanged(p, TimelineEdits.UpdateClips(p, ["c1"], c => c, "Edit"));
    }

    [Fact]
    public void TitleOpacityAndColourRangesClamp()
    {
        var p = new ProjectBuilder().With(V1, Title("t1", 0, 10), V("c1", 10, 10)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.UpdateClips(p, ["t1", "c1"], c => c switch
        {
            TitleClip t => t with { Opacity = -1, Title = t.Title with { Size = 0 } },
            MediaClip m => m with { Video = m.Video with { Color = new ColorAdjust(5, -1, 9, 0) } },
            _ => c,
        }, "Style"));
        var t1 = Assert.IsType<TitleClip>(ModelAssert.Clip(q, "t1"));
        Assert.Equal((0.0, 1.0), (t1.Opacity, t1.Title.Size));
        Assert.Equal(new ColorAdjust(1, 0, 3, 0.1), ModelAssert.Media(q, "c1").Video.Color);
    }

    [Fact]
    public void VideoEffectRangesClampAndABadKeyColourFallsBack()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 10)).Build();
        var wild = new VideoEffects(true, true, 400, -270, 10, 3, -4, 7, 2, true, true, "lime", 5, -1);
        var q = ModelAssert.Changed(p, TimelineEdits.UpdateClips(p, ["c1"], c => c is MediaClip m ? m with { Video = m.Video with { Effects = wild } } : c, "Effects"));
        Assert.Equal(
            new VideoEffects(true, true, 180, -180, 1000, 1, 0, 1, 1, true, true, "#00FF00", 1, 0),
            ModelAssert.Media(q, "c1").Video.Effects);

        var nan = new VideoEffects(false, false, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, false, false, "#ABCDEF", double.NaN, double.NaN);
        var r = ModelAssert.Changed(q, TimelineEdits.UpdateClips(q, ["c1"], c => c is MediaClip m ? m with { Video = m.Video with { Effects = nan } } : c, "Effects"));
        Assert.Equal(VideoEffects.None with { KeyColor = "#ABCDEF" }, ModelAssert.Media(r, "c1").Video.Effects);
        ModelAssert.Unchanged(r, TimelineEdits.UpdateClips(r, ["c1"], c => c is MediaClip m ? m with { Video = m.Video with { Effects = nan } } : c, "Effects"));
    }

    [Fact]
    public void AudioEffectRangesClampAndLowCutOffsSnapToOff()
    {
        var p = new ProjectBuilder().With(A1, A("a1", 0, 10)).Build();
        var wild = new AudioEffects(5000, 30000, -40, 25, true, -90, 50, true, 5, -3, true);
        var q = ModelAssert.Changed(p, TimelineEdits.UpdateClips(p, ["a1"], c => c is MediaClip m ? m with { Audio = m.Audio with { Effects = wild } } : c, "Effects"));
        Assert.Equal(new AudioEffects(2000, 20000, -20, 20, true, -60, 20, true, 0, -1, true), ModelAssert.Media(q, "a1").Audio.Effects);

        // A cut-off below its lowest audible value means off: 10 Hz high-pass and 150 Hz low-pass both snap to 0.
        var low = new AudioEffects(10, 150, 0, 0, false, -20, 4, false, -40, 0, false);
        var r = ModelAssert.Changed(q, TimelineEdits.UpdateClips(q, ["a1"], c => c is MediaClip m ? m with { Audio = m.Audio with { Effects = low } } : c, "Effects"));
        Assert.Equal(AudioEffects.None, ModelAssert.Media(r, "a1").Audio.Effects);

        var nan = new AudioEffects(double.NaN, double.NaN, double.NaN, double.NaN, false, double.NaN, double.NaN, false, double.NaN, double.NaN, false);
        ModelAssert.Unchanged(r, TimelineEdits.UpdateClips(r, ["a1"], c => c is MediaClip m ? m with { Audio = m.Audio with { Effects = nan } } : c, "Effects"));
        Assert.Same(ModelAssert.Media(r, "a1").Audio, ModelAssert.Media(TimelineEdits.UpdateClips(r, ["a1"], c => c, "Noop").Project, "a1").Audio);
    }

    [Fact]
    public void EnableAndDisable()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50)).Build();
        var r = TimelineEdits.SetEnabled(p, ["c1"], false);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("Disable", r.Label);
        Assert.False(ModelAssert.Clip(q, "c1").Enabled);
        ModelAssert.Unchanged(q, TimelineEdits.SetEnabled(q, ["c1"], false));
    }

    [Fact]
    public void LinkAndUnlink()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50), Title("t1", 50, 10)).With(A1, A("a1", 0, 50)).Build();
        var r = TimelineEdits.Link(_ids, p, ["c1", "a1", "t1"]);
        var q = ModelAssert.Changed(p, r);
        Assert.Equal("l_00001000", ModelAssert.Clip(q, "c1").LinkId);
        Assert.Equal("l_00001000", ModelAssert.Clip(q, "a1").LinkId);
        Assert.Null(ModelAssert.Clip(q, "t1").LinkId);
        Assert.Contains("Titles cannot be linked.", r.Warnings);

        var u = ModelAssert.Changed(q, TimelineEdits.Unlink(q, ["c1"]));
        Assert.Null(ModelAssert.Clip(u, "c1").LinkId);
        Assert.Null(ModelAssert.Clip(u, "a1").LinkId);
        ModelAssert.Unchanged(p, TimelineEdits.Link(_ids, p, ["c1"]));
    }

    [Fact]
    public void UnlinkKeepsGroupsOfTwoOrMore()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, link: "l1")).With(A1, A("a1", 0, 50, link: "l1")).With(A2, A("a2", 0, 50, link: "l1")).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Unlink(p, ["c1"]));
        Assert.Equal("l1", ModelAssert.Clip(q, "a1").LinkId);
        Assert.Equal("l1", ModelAssert.Clip(q, "a2").LinkId);
    }

    [Fact]
    public void MarkersStaySortedAndValidated()
    {
        var p = new ProjectBuilder().Marker("k1", 100).Build();
        var r = TimelineEdits.AddMarker(_ids, p, 50, "Drop", "not a colour");
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(["k_00001000", "k1"], q.Sequence.Markers.Select(m => m.Id));
        Assert.Equal(TimelineEdits.DefaultMarkerColor, q.Sequence.Markers[0].Color);
        Assert.Equal(["k_00001000"], r.Select.ToArray());

        var moved = ModelAssert.Changed(q, TimelineEdits.MoveMarker(q, "k_00001000", 150));
        Assert.Equal(["k1", "k_00001000"], moved.Sequence.Markers.Select(m => m.Id));
        var renamed = ModelAssert.Changed(moved, TimelineEdits.UpdateMarker(moved, "k1", m => m with { Name = "Intro", Color = "#00FF00", Frame = -5 }));
        Assert.Equal(new Marker("k1", 0, "Intro", "#00FF00"), renamed.Sequence.Markers[0]);
        var removed = ModelAssert.Changed(renamed, TimelineEdits.RemoveMarkers(renamed, ["k1"]));
        Assert.Single(removed.Sequence.Markers);
        ModelAssert.Unchanged(removed, TimelineEdits.MoveMarker(removed, "nope", 3));
    }

    [Fact]
    public void InOutClamps()
    {
        var p = new ProjectBuilder().Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetInOut(p, -5, 40));
        Assert.Equal((0L, 40L), (q.Sequence.InFrame!.Value, q.Sequence.OutFrame!.Value));
        var r = TimelineEdits.SetInOut(p, 50, 20);
        Assert.Equal(51, ModelAssert.Changed(p, r).Sequence.OutFrame);
        Assert.NotEmpty(r.Warnings);
        var cleared = TimelineEdits.SetInOut(q, null, null);
        Assert.Equal("Clear In/Out", cleared.Label);
        Assert.Null(ModelAssert.Changed(q, cleared).Sequence.InFrame);
        ModelAssert.Unchanged(p, TimelineEdits.SetInOut(p, null, null));
    }

    [Fact]
    public void ImportAddsAndReplacesMedia()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 90, media: Short, sourceFrame: 5)).With(A1, A("a1", 0, 90, media: Short, sourceFrame: 5)).Build();
        var added = Asset("m_new", "D:/new.mp4", 1_000_000, true, true);
        var q = ModelAssert.Changed(p, TimelineEdits.ImportMedia(p, [added]));
        Assert.Equal("Import", TimelineEdits.ImportMedia(p, [added]).Label);
        Assert.Contains(q.Media, m => m.Id == "m_new");

        // The re-probed file is 2 s (50 frames) and lost its audio.
        var shorter = Asset(Short, "D:/Footage/short.mov", 2_000_000, video: true, audio: false);
        var r = TimelineEdits.ImportMedia(q, [shorter]);
        var s = ModelAssert.Changed(q, r);
        Assert.Equal(45, ModelAssert.Clip(s, "c1").Duration);
        Assert.Empty(ModelAssert.Track(s, A1).Clips);
        Assert.NotEmpty(r.Warnings);
        ModelAssert.Unchanged(s, TimelineEdits.ImportMedia(s, [shorter]));
    }

    [Fact]
    public void RemoveMediaRemovesItsClips()
    {
        var p = new ProjectBuilder().With(V1, V("c1", 0, 50, media: Short), Title("t1", 50, 10)).With(A1, A("a1", 0, 50, media: Short)).Lock(A1).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.RemoveMedia(p, [Short]));
        Assert.DoesNotContain(q.Media, m => m.Id == Short);
        Assert.Equal(["t1"], ModelAssert.Track(q, V1).Clips.Select(c => c.Id));
        Assert.Empty(ModelAssert.Track(q, A1).Clips);
    }

    [Fact]
    public void SequenceSettingsConvertPositionsByTime()
    {
        var clip = A("a1", 25, 50) with { Audio = new AudioProps(0, 5, 10, [new VolumeKey(10, -3)], AudioEffects.None) };
        var p = new ProjectBuilder()
            .With(V1, V("c1", 0, 25, sourceFrame: 100), V("c2", 25, 25, sourceFrame: 300))
            .With(A1, clip)
            .Transition(V1, "x1", "c1", "c2", 10)
            .Marker("k1", 75)
            .InOut(10, 60)
            .Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetSequenceSettings(p, new SequenceSettings(1280, 720, FrameRate.Fps50)));
        Assert.Equal(new SequenceSettings(1280, 720, FrameRate.Fps50), q.Sequence.Settings);
        Assert.Equal([(0L, 50L), (50L, 50L)], ModelAssert.Spans(q, V1));
        Assert.Equal(20, ModelAssert.Track(q, V1).Transitions[0].Duration);
        var a1 = ModelAssert.Media(q, "a1");
        Assert.Equal((50L, 100L), (a1.Start, a1.Duration));
        Assert.Equal((10L, 20L), (a1.Audio.FadeInFrames, a1.Audio.FadeOutFrames));
        Assert.Equal(20, a1.Audio.Keys[0].Frame);
        Assert.Equal(150, q.Sequence.Markers[0].Frame);
        Assert.Equal((20L, 120L), (q.Sequence.InFrame!.Value, q.Sequence.OutFrame!.Value));
        Assert.Equal(100 * F, ModelAssert.Media(q, "c1").SourceInFlicks);
    }

    [Fact]
    public void SequenceSettingsKeepOneFrameClipsApart()
    {
        var p = new ProjectBuilder().Rate(FrameRate.Fps60).With(V1, Title("t1", 0, 1), Title("t2", 1, 1), Title("t3", 2, 1)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetSequenceSettings(p, new SequenceSettings(1920, 1080, FrameRate.Fps25)));
        Assert.Equal([(0L, 1L), (1L, 1L), (2L, 1L)], ModelAssert.Spans(q, V1));
    }

    [Fact]
    public void SequenceSettingsRoundSizesAndRates()
    {
        var p = new ProjectBuilder().Build();
        var r = TimelineEdits.SetSequenceSettings(p, new SequenceSettings(1081, 607, new FrameRate(new Rational(2997, 100))));
        var q = ModelAssert.Changed(p, r);
        Assert.Equal(new SequenceSettings(1082, 608, FrameRate.Fps2997), q.Sequence.Settings);
        Assert.Equal(2, r.Warnings.Length);
        ModelAssert.Unchanged(q, TimelineEdits.SetSequenceSettings(q, q.Sequence.Settings));
    }

    [Theory]
    [InlineData(100, 25, 50, 200)]
    [InlineData(1001, 30, 30000, 1000)]
    [InlineData(7, 25, 24, 7)]
    [InlineData(0, 60, 25, 0)]
    public void ConvertFrameRoundsToTheNearestFrame(long frame, int fromNum, int toNum, long expected)
    {
        var from = FrameRate.Supported.First(r => r.Value.Num == fromNum);
        var to = FrameRate.Supported.First(r => r.Value.Num == toNum);
        Assert.Equal(expected, TimelineEdits.ConvertFrame(frame, from, to));
    }
}
