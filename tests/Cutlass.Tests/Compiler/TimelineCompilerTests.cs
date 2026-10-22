using Cutlass.Compiler;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.Compiler.CompilerKit;

namespace Cutlass.Tests.Compiler;

/// <summary>TimelineCompiler behaviour beyond the goldens: validation, inputs, fonts, colours and warnings.</summary>
[UseCulture("de-DE")]
public sealed class TimelineCompilerTests
{
    private static readonly SequenceSettings Hd30 = new(1920, 1080, FrameRate.Fps30);

    [Theory]
    [InlineData(10, 10, 1920, 1080, 1, true, true)]
    [InlineData(-1, 10, 1920, 1080, 1, true, true)]
    [InlineData(0, 10, 1921, 1080, 1, true, true)]
    [InlineData(0, 10, 0, 1080, 1, true, true)]
    [InlineData(0, 10, 1920, 1080, 0, true, true)]
    [InlineData(0, 10, 1920, 1080, 1, false, false)]
    public void InvalidRequestsAreRejected(long start, long end, int width, int height, int step, bool video, bool audio)
    {
        var project = CompilerKit.WorkedExample();
        var request = new RenderRequest(start, end, width, height, RenderPurpose.Export, video, audio, false, AudioLayout.Mixed, step, OutputTail.ForExport("yuv420p"), true);

        Assert.Throws<ArgumentException>(() => TimelineCompiler.Compile(project, TestCatalog.For(project), request));
    }

    [Fact]
    public void EmptyTimelineIsABlackFrameAndSilence()
    {
        var project = Project(Hd30, []);

        var render = TimelineCompiler.Compile(project, new TestCatalog(), Export(0, 3));

        Assert.Equal(
            "color=c=black:s=1920x1080:r=30,trim=end_frame=3,format=yuv420p,setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv,format=yuv420p[vout];\n" +
            "anullsrc=r=48000:cl=stereo,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,atrim=end_sample=4800[aout]",
            render.GraphText);
        Assert.Empty(render.Inputs);
    }

    [Fact]
    public void InputsMergeOnPathAndSeekKeepingTheLargerSpan()
    {
        var project = Project(
            Hd30,
            [VideoAsset("m_a", "D:/Clips/a.mp4")],
            VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", F)]),
            VideoTrack("tr_v2", [V("c_2", 0, 60, "m_a", F, video: VideoProps.Default with { Transform = Transform.Identity with { Scale = 0.5 } })]),
            AudioTrack("tr_a1", [A("c_3", 0, 90, "m_a", F)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 90));

        var input = Assert.Single(render.Inputs);
        Assert.Equal(new InputSpec("D:/Clips/a.mp4", 1_000_000, 3_100_000, false, null), input);
        Assert.Contains("[0:0]", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("[0:1]", render.GraphText, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicatedClipGetsItsOwnInputWhileLinkedAudioAndVideoShareOne()
    {
        // The same source range used again ten seconds later must not share the first use's input: ffmpeg would buffer
        // ten seconds of decoded frames for it (TECH_BRIEF 7.7).
        var project = Project(
            Hd30,
            [VideoAsset("m_a", "D:/Clips/a.mp4")],
            VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", F, link: "l_1"), V("c_2", 300, 30, "m_a", F, link: "l_2")]),
            AudioTrack("tr_a1", [A("c_3", 0, 30, "m_a", F, link: "l_1"), A("c_4", 300, 30, "m_a", F, link: "l_2")]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 330));

        Assert.Equal(2, render.Inputs.Count);
        Assert.All(render.Inputs, i => Assert.Equal(1_000_000, i.SeekMicros));
        foreach (var label in new[] { "[0:0]", "[0:1]", "[1:0]", "[1:1]" })
        {
            Assert.Contains(label, render.GraphText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UsesReadAtDifferentSpeedsDoNotShareAnInput()
    {
        var project = Project(
            Hd30,
            [VideoAsset("m_a", "D:/Clips/a.mp4")],
            VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", F, speed: 200)]),
            VideoTrack("tr_v2", [V("c_2", 0, 30, "m_a", F)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, audio: false));

        Assert.Equal(2, render.Inputs.Count);
    }

    [Fact]
    public void SpeedChangedAudioIsPreRolledAndTheLeadIsCut()
    {
        var project = Project(
            Hd30,
            [VideoAsset("m_a", "D:/Clips/a.mp4")],
            AudioTrack("tr_a1", [A("c_1", 0, 30, "m_a", F, speed: 50)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, video: false));

        var input = Assert.Single(render.Inputs);
        Assert.Equal(800_000, input.SeekMicros);
        Assert.Contains(
            "[0:1]aresample=48000:async=1:first_pts=0,atempo=0.500000,atrim=start_sample=18574,asetpts=PTS-STARTPTS,aformat=",
            render.GraphText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PreRollBeforeTheFileStartIsPaddedWithSilence()
    {
        var project = Project(
            Hd30,
            [VideoAsset("m_a", "D:/Clips/a.mp4")],
            AudioTrack("tr_a1", [A("c_1", 0, 30, "m_a", F / 20, speed: 200)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, video: false));

        // 0.05 s is available before the clip, so 0.15 s (7200 samples) of silence completes the 0.2 s pre-roll.
        Assert.Equal(0, Assert.Single(render.Inputs).SeekMicros);
        Assert.Contains(
            "aresample=48000:async=1:first_pts=0,adelay=7200S:all=1,atempo=2.000000,atrim=start_sample=4582,",
            render.GraphText,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(25, 38400 - 1523)]
    [InlineData(49, 19592 - 806)]
    [InlineData(50, 19200 - 626)]
    [InlineData(52, 18462 - 602)]
    [InlineData(99, 9697 - 313)]
    [InlineData(150, 6400 - 240)]
    [InlineData(200, 4800 - 218)]
    [InlineData(201, 4776 - 645)]
    [InlineData(300, 3200 - 454)]
    [InlineData(400, 2400 - 409)]
    public void AtempoHeadTrimIsThePreRollMinusTheCalibratedLead(int percent, long expected)
    {
        Assert.Equal(expected, AtempoPreRoll.HeadTrimSamples(percent));
    }

    [Fact]
    public void FontsResolveThroughTheCatalogThenTheBundledNames()
    {
        var spec = TitleSpec.Default with { Text = "x", Font = "C:/Users/me/Fonts/Fancy.otf" };
        var project = Project(
            Hd30,
            [],
            VideoTrack("tr_v1", [Title("c_1", 0, 10, spec), Title("c_2", 10, 10, TitleSpec.Default with { Font = "builtin:Inter-SemiBold" })]),
            VideoTrack("tr_v2", [Title("c_3", 0, 10, TitleSpec.Default with { Font = "builtin:Comic" })]));
        var catalog = new TestCatalog();
        catalog.Fonts["C:/Users/me/Fonts/Fancy.otf"] = "font_0a1b2c3d.otf";

        var render = TimelineCompiler.Compile(project, catalog, Export(0, 20, audio: false));

        Assert.Contains("fontfile=font_0a1b2c3d.otf:", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("fontfile=Inter-SemiBold.ttf:", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("fontfile=Inter-Regular.ttf:", render.GraphText, StringComparison.Ordinal);
        Assert.Contains(render.Warnings, w => w.Contains("builtin:Comic", StringComparison.Ordinal));
        Assert.DoesNotContain("Fancy", render.GraphText, StringComparison.Ordinal);
    }

    [Fact]
    public void UnresolvedUserFontsFallBackWithAWarningAndNeverEnterTheGraph()
    {
        var spec = TitleSpec.Default with { Font = "D:/it's [odd]/font.ttf" };
        var project = Project(Hd30, [], VideoTrack("tr_v1", [Title("c_1", 0, 10, spec)]));

        var render = TimelineCompiler.Compile(project, new TestCatalog(), Export(0, 10, audio: false));

        Assert.Contains("fontfile=Inter-Regular.ttf:", render.GraphText, StringComparison.Ordinal);
        Assert.Single(render.Warnings);
        Assert.DoesNotContain("odd", render.GraphText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#FFFFFFFF", "0xFFFFFF")]
    [InlineData("#00000099", "0x000000@0.6")]
    [InlineData("#ff8000", "0xFF8000")]
    [InlineData("#12345600", "0x123456@0")]
    [InlineData("#ABCDEF80", "0xABCDEF@0.502")]
    [InlineData("red", null)]
    [InlineData("#GGGGGG", null)]
    [InlineData("#12345", null)]
    public void TitleColours(string value, string? expected)
    {
        Assert.Equal(expected, TitleEmitter.FormatColor(value));
    }

    [Fact]
    public void BadColoursWarnAndUseTheDefault()
    {
        var project = Project(Hd30, [], VideoTrack("tr_v1", [Title("c_1", 0, 10, TitleSpec.Default with { Color = "white" })]));

        var render = TimelineCompiler.Compile(project, new TestCatalog(), Export(0, 10, audio: false));

        Assert.Contains("fontcolor=0xFFFFFF:", render.GraphText, StringComparison.Ordinal);
        Assert.Contains(render.Warnings, w => w.Contains("white", StringComparison.Ordinal));
    }

    [Fact]
    public void ManyInputsWarnAboutMemory()
    {
        var media = Enumerable.Range(0, 70).Select(i => VideoAsset($"m_{i}", $"D:/Clips/{i}.mp4")).ToArray();
        var clips = media.Select((m, i) => (Clip)V($"c_{i}", i * 2, 2, m.Id)).ToArray();
        var project = Project(Hd30, media, VideoTrack("tr_v1", clips));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 140, audio: false));

        Assert.Equal(70, render.Inputs.Count);
        Assert.Contains(render.Warnings, w => w.Contains("70 inputs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(100, new double[0])]
    [InlineData(25, new[] { 0.5, 0.5 })]
    [InlineData(50, new[] { 0.5 })]
    [InlineData(75, new[] { 0.75 })]
    [InlineData(150, new[] { 1.5 })]
    [InlineData(200, new[] { 2.0 })]
    [InlineData(300, new[] { 2.0, 1.5 })]
    [InlineData(400, new[] { 2.0, 2.0 })]
    public void AtempoStepsStayWithinRange(int percent, double[] expected)
    {
        var factors = AudioEmitter.AtempoFactors(percent);

        Assert.Equal(expected, factors);
        Assert.All(factors, f => Assert.InRange(f, 0.5, 2.0));
    }

    [Fact]
    public void LimiterIsOptional()
    {
        var c = GoldenCases.Get("single_clip");

        var render = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request with { LimitPeaks = false });

        Assert.DoesNotContain("alimiter", render.GraphText, StringComparison.Ordinal);
    }

    [Fact]
    public void XfadeNamesCoverEveryVideoKind()
    {
        Assert.Equal("fade", VideoEmitter.XfadeName(TransitionKind.Crossfade));
        Assert.Equal("fadeblack", VideoEmitter.XfadeName(TransitionKind.DipToBlack));
        Assert.Equal("fadewhite", VideoEmitter.XfadeName(TransitionKind.DipToWhite));
        Assert.Equal("wipeleft", VideoEmitter.XfadeName(TransitionKind.WipeLeft));
        Assert.Equal("wiperight", VideoEmitter.XfadeName(TransitionKind.WipeRight));
        Assert.Equal("slideleft", VideoEmitter.XfadeName(TransitionKind.SlideLeft));
        Assert.Equal("slideright", VideoEmitter.XfadeName(TransitionKind.SlideRight));
        Assert.Equal("circleopen", VideoEmitter.XfadeName(TransitionKind.CircleOpen));
        Assert.Equal("fade", VideoEmitter.XfadeName(TransitionKind.AudioCrossfade));
    }
}
