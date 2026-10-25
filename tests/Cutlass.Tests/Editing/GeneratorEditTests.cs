using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Editing;

/// <summary>Generator clips (effects batch 5) through the Core edits: placement, split, paste, refusals and rates.</summary>
public sealed class GeneratorEditTests
{
    private readonly SequentialIdGenerator _ids = new(1000);

    [Fact]
    public void BarsThenFootageAdoptTheFootageSizeAndRate()
    {
        // DESIGN 2.1: only media uses a sequence, so bars (or a title) placed first leave it adoptable.
        var p = new ProjectBuilder().Fresh().Build();
        var withBars = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [new ClipPlacement(V2, Generator("g", 0, 60, GeneratorKind.Bars))], 0, EditMode.Overwrite));
        var withTitle = ModelAssert.Changed(withBars, TimelineEdits.Place(_ids, withBars, [new ClipPlacement(V2, Title("t", 0, 30))], 100, EditMode.Overwrite));
        Assert.False(withTitle.Sequence.HasHadClips);
        Assert.Equal(p.Sequence.Settings, withTitle.Sequence.Settings);

        var r = TimelineEdits.Place(_ids, withTitle, TimelineEdits.SourcePlacements(withTitle, Uhd, 0, 50, V1, A1), 0, EditMode.Overwrite);
        var q = ModelAssert.Changed(withTitle, r);
        Assert.Equal(new SequenceSettings(3840, 2160, FrameRate.Fps2997), q.Sequence.Settings);
        Assert.True(q.Sequence.HasHadClips);
        Assert.Contains(r.Warnings, w => w.StartsWith("Sequence set to 3840x2160", StringComparison.Ordinal));
        Assert.IsType<GeneratorClip>(ModelAssert.Track(q, V2).Clips[0]);
        ModelAssert.Valid(q);
    }

    [Fact]
    public void FootageThenBarsStillAdoptsTheFootage()
    {
        var p = new ProjectBuilder().Fresh().Build();
        var withFootage = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, TimelineEdits.SourcePlacements(p, Uhd, 0, 50, V1, A1), 0, EditMode.Overwrite));
        Assert.Equal(new SequenceSettings(3840, 2160, FrameRate.Fps2997), withFootage.Sequence.Settings);
        Assert.True(withFootage.Sequence.HasHadClips);

        var q = ModelAssert.Changed(withFootage, TimelineEdits.Place(_ids, withFootage, [new ClipPlacement(V2, Generator("g", 0, 60, GeneratorKind.Bars))], 0, EditMode.Overwrite));
        Assert.Equal(withFootage.Sequence.Settings, q.Sequence.Settings);
        Assert.True(q.Sequence.HasHadClips);

        // Removing every media clip keeps the sequence used: the flag is sticky.
        var emptied = ModelAssert.Changed(q, TimelineEdits.Lift(q, [.. ModelAssert.Track(q, V1).Clips.Select(c => c.Id), .. ModelAssert.Track(q, A1).Clips.Select(c => c.Id)]));
        Assert.True(emptied.Sequence.HasHadClips);
    }

    [Fact]
    public void PlacingChecksTheTrackKindOfTheGenerator()
    {
        var p = new ProjectBuilder().Build();
        var wrong = TimelineEdits.Place(_ids, p, [new ClipPlacement(A1, Generator("g", 0, 50)), new ClipPlacement(V1, Generator("t", 0, 50, GeneratorKind.Tone))], 10, EditMode.Overwrite);
        ModelAssert.Unchanged(p, wrong);
        Assert.Contains("Color Matte goes on video tracks.", wrong.Warnings);
        Assert.Contains("Tone goes on audio tracks.", wrong.Warnings);

        var r = TimelineEdits.Place(_ids, p, [new ClipPlacement(V2, Generator("g", 0, 50, GeneratorKind.Bars) with { LinkId = "l_x" }), new ClipPlacement(A2, Generator("t", 0, 50, GeneratorKind.Tone, toneHz: 440))], 10, EditMode.Overwrite);
        var q = ModelAssert.Changed(p, r);
        var bars = Assert.IsType<GeneratorClip>(Assert.Single(ModelAssert.Track(q, V2).Clips));
        var tone = Assert.IsType<GeneratorClip>(Assert.Single(ModelAssert.Track(q, A2).Clips));
        Assert.Equal((GeneratorKind.Bars, 10L, 50L, null as string), (bars.Kind, bars.Start, bars.Duration, bars.LinkId));
        Assert.Equal((GeneratorKind.Tone, 440.0), (tone.Kind, tone.ToneHz));
        Assert.Equal(["Bars", "Tone"], r.Select.Select(id => TimelineQuery.DisplayName(q, ModelAssert.Clip(q, id))));
    }

    [Fact]
    public void ColourFrequencyAndPropsClamp()
    {
        var p = new ProjectBuilder().With(V1, Generator("g1", 0, 20)).With(A1, Generator("g2", 0, 20, GeneratorKind.Tone)).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.UpdateClips(p, ["g1", "g2"], c => c is GeneratorClip g
            ? g with
            {
                Color = "red",
                ToneHz = 99_999,
                Video = g.Video with { Opacity = 3, FadeInFrames = 15, FadeOutFrames = 15 },
                Audio = g.Audio with { GainDb = 40, FadeInFrames = -5 },
            }
            : c, "Style"));
        var g1 = Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, "g1"));
        var g2 = Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, "g2"));
        Assert.Equal((GeneratorClip.DefaultColor, GeneratorClip.MaxToneHz, 1.0, 15L, 5L), (g1.Color, g1.ToneHz, g1.Video.Opacity, g1.Video.FadeInFrames, g1.Video.FadeOutFrames));
        Assert.Equal((TimelineEdits.MaxGainDb, 0L), (g2.Audio.GainDb, g2.Audio.FadeInFrames));

        var r = ModelAssert.Changed(q, TimelineEdits.UpdateClips(q, ["g1", "g2"], c => c is GeneratorClip g ? g with { Color = "#12ab9F", ToneHz = 5 } : c, "Style"));
        Assert.Equal("#12ab9F", Assert.IsType<GeneratorClip>(ModelAssert.Clip(r, "g1")).Color);
        Assert.Equal(GeneratorClip.MinToneHz, Assert.IsType<GeneratorClip>(ModelAssert.Clip(r, "g2")).ToneHz);
        var s = ModelAssert.Changed(r, TimelineEdits.UpdateClips(r, ["g2"], c => c is GeneratorClip g ? g with { ToneHz = double.NaN } : c, "Style"));
        Assert.Equal(GeneratorClip.DefaultToneHz, Assert.IsType<GeneratorClip>(ModelAssert.Clip(s, "g2")).ToneHz);
        ModelAssert.Unchanged(s, TimelineEdits.UpdateClips(s, ["g1", "g2"], c => c, "Noop"));
    }

    [Fact]
    public void AKindChangeStaysOnTheSameTrackKind()
    {
        var p = new ProjectBuilder().With(V1, Generator("g1", 0, 20, color: "#FF0000")).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.UpdateClips(p, ["g1"], c => ((GeneratorClip)c) with { Kind = GeneratorKind.Bars }, "Kind"));
        Assert.Equal(GeneratorKind.Bars, Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, "g1")).Kind);

        var r = TimelineEdits.UpdateClips(q, ["g1"], c => ((GeneratorClip)c) with { Kind = GeneratorKind.Tone }, "Kind");
        ModelAssert.Valid(r.Project);
        Assert.Equal(GeneratorKind.Bars, Assert.IsType<GeneratorClip>(ModelAssert.Clip(r.Project, "g1")).Kind);
        Assert.Contains("Position, length, links, source and speed change only through their own edits.", r.Warnings);
    }

    [Fact]
    public void SplitKeepsKindColourFrequencyAndPropsAndSplitsTheKeys()
    {
        var matte = Generator("g1", 0, 40, color: "#336699") with { Video = VideoProps.Default with { Opacity = 0.5, FadeInFrames = 5, FadeOutFrames = 6 } };
        var tone = Generator("g2", 0, 40, GeneratorKind.Tone, toneHz: 440) with
        {
            Audio = AudioProps.Default with { GainDb = -6, FadeInFrames = 4, FadeOutFrames = 8, Keys = [new VolumeKey(0, 0), new VolumeKey(32, -16)] },
        };
        var p = new ProjectBuilder().With(V1, matte).With(A1, tone).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Split(_ids, p, [V1, A1], 17));

        var v = ModelAssert.Track(q, V1).Clips.Cast<GeneratorClip>().ToList();
        Assert.Equal([(0L, 17L), (17L, 23L)], v.Select(c => (c.Start, c.Duration)));
        Assert.All(v, c => Assert.Equal((GeneratorKind.ColorMatte, "#336699", 0.5), (c.Kind, c.Color, c.Video.Opacity)));
        Assert.Equal((5L, 0L), (v[0].Video.FadeInFrames, v[0].Video.FadeOutFrames));
        Assert.Equal((0L, 6L), (v[1].Video.FadeInFrames, v[1].Video.FadeOutFrames));

        var a = ModelAssert.Track(q, A1).Clips.Cast<GeneratorClip>().ToList();
        Assert.All(a, c => Assert.Equal((GeneratorKind.Tone, 440.0, -6.0), (c.Kind, c.ToneHz, c.Audio.GainDb)));
        Assert.Equal((4L, 0L, 0L, 8L), (a[0].Audio.FadeInFrames, a[0].Audio.FadeOutFrames, a[1].Audio.FadeInFrames, a[1].Audio.FadeOutFrames));
        Assert.Equal([new VolumeKey(0, 0), new VolumeKey(16, -8)], a[0].Audio.Keys.ToArray());
        Assert.Equal([new VolumeKey(0, -8.5), new VolumeKey(15, -16)], a[1].Audio.Keys.ToArray());
    }

    [Fact]
    public void TrimmingTheHeadMovesTheKeysWithTheClip()
    {
        var tone = Generator("g1", 0, 40, GeneratorKind.Tone) with { Audio = AudioProps.Default with { Keys = [new VolumeKey(5, -3), new VolumeKey(30, -9)] } };
        var p = new ProjectBuilder().With(A1, tone).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Trim(p, "g1", ClipEdge.Start, 10, ripple: false, includeLinked: false));
        var g = Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, "g1"));
        Assert.Equal((10L, 30L), (g.Start, g.Duration));
        Assert.Equal([new VolumeKey(20, -9)], g.Audio.Keys.ToArray());

        // Generators are unbounded: the head extends past frame 0 of any source.
        var r = ModelAssert.Changed(q, TimelineEdits.Trim(q, "g1", ClipEdge.Start, -10, ripple: false, includeLinked: false));
        Assert.Equal((0L, 40L), (ModelAssert.Clip(r, "g1").Start, ModelAssert.Clip(r, "g1").Duration));
    }

    [Fact]
    public void CopyAndPasteKeepTheGeneratorAndFallBackToATrackOfItsKind()
    {
        var tone = Generator("g2", 10, 20, GeneratorKind.Tone, toneHz: 250);
        var p = new ProjectBuilder().With(V2, Generator("g1", 0, 20, color: "#00FF00")).With(A2, tone).Build();
        var clipboard = ClipClipboard.Copy(p, ["g1", "g2"]);
        var removed = ModelAssert.Changed(p, TimelineEdits.RemoveTrack(p, A2));
        removed = ModelAssert.Changed(removed, TimelineEdits.RemoveTrack(removed, V2));
        Assert.Equal([V1, A1], clipboard.ResolvePlacements(removed).Select(i => i.TrackId));

        var r = clipboard.Paste(_ids, removed, 100, EditMode.Overwrite);
        var q = ModelAssert.Changed(removed, r);
        var pasted = r.Select.Select(id => Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, id))).ToList();
        Assert.Equal([(GeneratorKind.ColorMatte, 100L, "#00FF00"), (GeneratorKind.Tone, 110L, GeneratorClip.DefaultColor)], pasted.Select(c => (c.Kind, c.Start, c.Color)));
        Assert.Equal(250, pasted[1].ToneHz);
    }

    [Fact]
    public void SpeedSlipAndLinkRefuseGenerators()
    {
        var p = new ProjectBuilder().With(V1, Generator("g1", 0, 20), V("c1", 20, 20)).With(A1, A("a1", 20, 20)).Build();
        var speed = TimelineEdits.SetSpeed(p, ["g1"], 200, rippleFollowing: true);
        ModelAssert.Unchanged(p, speed);
        Assert.Contains("Generators have no speed.", speed.Warnings);

        var slip = TimelineEdits.Slip(p, "g1", 5, includeLinked: false);
        ModelAssert.Unchanged(p, slip);
        Assert.Contains("Generators have no source to slip.", slip.Warnings);

        var link = TimelineEdits.Link(_ids, p, ["g1", "c1", "a1"]);
        var q = ModelAssert.Changed(p, link);
        Assert.Contains("Generators cannot be linked.", link.Warnings);
        Assert.Null(ModelAssert.Clip(q, "g1").LinkId);
        Assert.NotNull(ModelAssert.Clip(q, "c1").LinkId);
    }

    [Fact]
    public void ARateChangeScalesFadesAndKeys()
    {
        var tone = Generator("g1", 25, 50, GeneratorKind.Tone) with { Audio = AudioProps.Default with { FadeInFrames = 5, Keys = [new VolumeKey(10, -3)] } };
        var matte = Generator("g2", 0, 50) with { Video = VideoProps.Default with { FadeOutFrames = 10 } };
        var p = new ProjectBuilder().With(V1, matte).With(A1, tone).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.SetSequenceSettings(p, new SequenceSettings(1280, 720, FrameRate.Fps50)));
        var g1 = Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, "g1"));
        Assert.Equal((50L, 100L, 10L, 20L), (g1.Start, g1.Duration, g1.Audio.FadeInFrames, g1.Audio.Keys[0].Frame));
        Assert.Equal(20, Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, "g2")).Video.FadeOutFrames);
    }

    [Fact]
    public void OverwriteTrimsAGeneratorAndKeepsItsFadesInRange()
    {
        var p = new ProjectBuilder().With(V1, Generator("g1", 0, 40) with { Video = VideoProps.Default with { FadeInFrames = 10, FadeOutFrames = 10 } }).Build();
        var q = ModelAssert.Changed(p, TimelineEdits.Place(_ids, p, [new ClipPlacement(V1, Generator("g", 0, 10, GeneratorKind.Bars))], 35, EditMode.Overwrite));
        var g1 = Assert.IsType<GeneratorClip>(ModelAssert.Clip(q, "g1"));
        Assert.Equal((35L, 10L, 10L), (g1.Duration, g1.Video.FadeInFrames, g1.Video.FadeOutFrames));
        Assert.Equal(GeneratorKind.Bars, Assert.IsType<GeneratorClip>(ModelAssert.Track(q, V1).Clips[1]).Kind);
    }
}
