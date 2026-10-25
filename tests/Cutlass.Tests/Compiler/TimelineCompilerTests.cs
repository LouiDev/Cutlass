using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
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

    // The xfade transitions of ffmpeg 6.0 (the invariant: only filters and options that exist in 6.0). The 6.1 additions
    // hlwind, hrwind, vuwind, vdwind, cover* and reveal* are deliberately absent.
    private static readonly HashSet<string> Allowed60 =
    [
        "fade", "wipeleft", "wiperight", "wipeup", "wipedown", "slideleft", "slideright", "slideup", "slidedown", "circlecrop",
        "rectcrop", "distance", "fadeblack", "fadewhite", "radial", "smoothleft", "smoothright", "smoothup", "smoothdown",
        "circleopen", "circleclose", "vertopen", "vertclose", "horzopen", "horzclose", "dissolve", "pixelize", "diagtl", "diagtr",
        "diagbl", "diagbr", "hlslice", "hrslice", "vuslice", "vdslice", "hblur", "fadegrays", "wipetl", "wipetr", "wipebl", "wipebr",
        "squeezeh", "squeezev", "zoomin", "fadefast", "fadeslow",
    ];

    private static readonly string[] EffectFilterNames =
        ["hflip", "vflip", "colortemperature", "colorbalance", "hue=", "gblur", "unsharp", "vignette", "chromakey", "rotate="];

    [Fact]
    public void NeutralEffectsEmitNoEffectFilter()
    {
        var project = Project(Hd30, [VideoAsset("m_a", "D:/Clips/a.mp4")], VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", 0)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, audio: false));

        Assert.Equal(VideoEffects.None, ModelAssert.Media(project, "c_1").Video.Effects);
        Assert.All(EffectFilterNames, name => Assert.DoesNotContain(name, render.GraphText, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryEffectEmitsInTheDocumentedOrder()
    {
        var video = VideoProps.Default with { Color = ColorAdjust.Neutral with { Saturation = 1.2 }, Effects = GoldenCases.AllEffects };
        var project = Project(Hd30, [VideoAsset("m_a", "D:/Clips/a.mp4")], VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", 0, video: video)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, audio: false));

        Assert.Contains(
            "scale=w=1920:h=1080:flags=bicubic:out_color_matrix=bt709:out_range=tv,"
            + "hflip,vflip,colortemperature=temperature=4500.000000,colorbalance=gm=-0.250000,scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,eq=saturation=1.200000,"
            + "hue=h=30.000000:s=0,gblur=sigma=2.000000,unsharp=lx=5:ly=5:la=0.750000,vignette=a=0.628319:dither=0,"
            + "setsar=1,format=yuva420p,chromakey=color=0x00FF00:similarity=0.200000:blend=0.050000,"
            + "rotate=a=12.500000*PI/180:fillcolor=black@0,",
            render.GraphText,
            StringComparison.Ordinal);
        Assert.Empty(render.Warnings);
    }

    [Fact]
    public void GrayscaleAloneAndABadKeyColourAreHandled()
    {
        var video = VideoProps.Default with { Effects = VideoEffects.None with { Grayscale = true, KeyEnabled = true, KeyColor = "lime" } };
        var project = Project(Hd30, [VideoAsset("m_a", "D:/Clips/a.mp4")], VideoTrack("tr_v1", [V("c_1", 0, 30, "m_a", 0, video: video)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, audio: false));

        Assert.Contains(",hue=s=0,setsar=1,", render.GraphText, StringComparison.Ordinal);
        Assert.DoesNotContain("chromakey", render.GraphText, StringComparison.Ordinal);
        Assert.Contains(render.Warnings, w => w.Contains("key colour 'lime'", StringComparison.Ordinal));
    }

    [Fact]
    public void FrozenClipTrimsToOneFrameBeforeTheClonePadAndGetsItsOwnInput()
    {
        // Effects batch 4 (TECH_BRIEF 7.3): the frozen chain is fps, trim to one frame, clone pad, trim to the segment;
        // the moving clip of the same source at the same start frame and seek keeps its own input (speed marker 0).
        var golden = GoldenCases.Get("freeze_frame");
        var render = TimelineCompiler.Compile(golden.Project, golden.Catalog, golden.Request);

        Assert.Contains(
            "fps=30:start_time=0:round=up,trim=end_frame=1,tpad=stop_mode=clone:stop=30,trim=end_frame=30,",
            render.GraphText,
            StringComparison.Ordinal);
        Assert.Contains("fps=30:start_time=0:round=up,tpad=stop_mode=clone:stop=30,trim=end_frame=30,", render.GraphText, StringComparison.Ordinal);
        Assert.Equal(2, render.Inputs.Count);
        Assert.Equal(render.Inputs[0].SeekMicros, render.Inputs[1].SeekMicros);
        Assert.DoesNotContain("setpts=PTS/", render.GraphText, StringComparison.Ordinal);
        Assert.Empty(render.Warnings);

        // A frozen clip ignores its speed: at 200 percent the chain has no speed setpts, and the seek, the span and the
        // whole graph equal the speed-100 compile.
        var fast = golden.Project with
        {
            Sequence = golden.Project.Sequence with
            {
                Tracks = [.. golden.Project.Sequence.Tracks.Select(t => t with { Clips = [.. t.Clips.Select(c => c is MediaClip { Freeze: true } m ? m with { SpeedPercent = 200 } : c)] })],
            },
        };
        Assert.Equal(200, fast.Sequence.Tracks.SelectMany(t => t.Clips).OfType<MediaClip>().Single(c => c.Id == "c_f").SpeedPercent);
        var fastRender = TimelineCompiler.Compile(fast, golden.Catalog, golden.Request);
        Assert.DoesNotContain("setpts=PTS/2", fastRender.GraphText, StringComparison.Ordinal);
        Assert.DoesNotContain(")/2,", fastRender.GraphText, StringComparison.Ordinal);
        Assert.Equal(render.Inputs[0], fastRender.Inputs[0]);
        Assert.Equal(render.Inputs.Count, fastRender.Inputs.Count);
        Assert.Equal(render.GraphText, fastRender.GraphText);

        // A window starting inside the frozen clip still seeks to the frozen frame, and a frozen still is a plain still.
        var later = TimelineCompiler.Compile(golden.Project, golden.Catalog, Export(10, 30, audio: false));
        Assert.Equal(render.Inputs[0].SeekMicros, later.Inputs[0].SeekMicros);
        var still = GoldenCases.Get("still");
        var frozenStill = still.Project with
        {
            Sequence = still.Project.Sequence with
            {
                Tracks = [.. still.Project.Sequence.Tracks.Select(t => t with { Clips = [.. t.Clips.Select(c => c is MediaClip m ? m with { Freeze = true } : c)] })],
            },
        };
        Assert.Equal(TimelineCompiler.Compile(still.Project, still.Catalog, still.Request).GraphText, TimelineCompiler.Compile(frozenStill, still.Catalog, still.Request).GraphText);
    }

    [Fact]
    public void NeutralAudioEffectsKeepTheHistoricalAudioChain()
    {
        var project = Project(Hd30, [AudioAsset("m_music", "D:/Audio/music.wav")], AudioTrack("tr_a1", [A("c_1", 0, 30, "m_music", 0)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, video: false));

        Assert.Equal(AudioEffects.None, ModelAssert.Media(project, "c_1").Audio.Effects);
        Assert.Contains("channel_layouts=stereo,apad=whole_len=", render.GraphText, StringComparison.Ordinal);
        Assert.All(["highpass", "lowpass", "bass=", "treble=", "agate", "acompressor", "pan="], name => Assert.DoesNotContain(name, render.GraphText, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryAudioEffectSitsBetweenAformatAndApadInTheDocumentedOrder()
    {
        var audio = AudioProps.Default with { Effects = GoldenCases.AllAudioEffects };
        var project = Project(Hd30, [AudioAsset("m_music", "D:/Audio/music.wav")], AudioTrack("tr_a1", [A("c_1", 0, 30, "m_music", 0, audio: audio)]));

        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, video: false));

        Assert.Contains(
            ",aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,"
            + "highpass=f=80.000000,lowpass=f=12000.000000,bass=g=3.000000:f=100,treble=g=-2.000000:f=3000,"
            + "agate=threshold=0.003162:ratio=4:attack=5:release=100:range=0.001000,"
            + "acompressor=threshold=0.063096:ratio=3.000000:attack=5:release=50:knee=1:makeup=1,"
            + "pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0+0.5*c1,apad=whole_len=",
            render.GraphText,
            StringComparison.Ordinal);
        Assert.Empty(render.Warnings);
    }

    [Fact]
    public void GeneratorsEmitLavfiSourcesAndNoInputs()
    {
        // Effects batch 5 (TECH_BRIEF 7.3, generators): the matte is drawn as rgb24 and scaled with the BT.709 output
        // options like a media clip (a no-op resize at canvas size), the transformed bars scale and place like a media
        // clip, the tone feeds the shared audio tail.
        var golden = GoldenCases.Get("generators");
        var render = TimelineCompiler.Compile(golden.Project, golden.Catalog, golden.Request);

        Assert.Empty(render.Inputs);
        Assert.Empty(render.Warnings);
        Assert.Contains(
            "color=c=0x3366CC:s=1920x1080:r=30,trim=end_frame=60,format=rgb24,scale=w=1920:h=1080:flags=bicubic:out_color_matrix=bt709:out_range=tv,setsar=1,format=yuva420p,lut=a=val*0.800000,fade=t=in:",
            render.GraphText,
            StringComparison.Ordinal);
        Assert.Contains("smptehdbars=s=1920x1080:r=30,trim=end_frame=60,scale=w=960:h=540:flags=bicubic:out_color_matrix=bt709:out_range=tv,setsar=1,format=yuva420p,", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("overlay=x=580:y=220:", render.GraphText, StringComparison.Ordinal);
        Assert.Contains(
            "aevalsrc=0.125*sin(2*PI*440.000000*(n+0)/48000)|0.125*sin(2*PI*440.000000*(n+0)/48000):s=48000:c=stereo,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_len=96000,atrim=end_sample=96000,volume=-6.000000dB,afade=t=in:ss=0:ns=24000:curve=tri,",
            render.GraphText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ATonePhaseCountsFromTheTimelineSoWindowsAndSplitsJoin()
    {
        // The source sample n is absolute timeline sample N0 + n with N0 = SamplesAt(segment start), so a later window
        // starts at its own sample and a split half continues the phase without generating samples to discard.
        var tone = GeneratorClip.Create("c_t", 10, 60, GeneratorKind.Tone);
        var project = Project(new SequenceSettings(1920, 1080, FrameRate.Fps30), [], VideoTrack("tr_v1", []), AudioTrack("tr_a1", [tone]));
        var later = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(30, 60, video: false));
        Assert.Contains(
            "aevalsrc=0.125*sin(2*PI*1000.000000*(n+48000)/48000)|0.125*sin(2*PI*1000.000000*(n+48000)/48000):s=48000:c=stereo,aformat=",
            later.GraphText,
            StringComparison.Ordinal);
        Assert.DoesNotContain("atrim=start_sample", later.GraphText, StringComparison.Ordinal);

        var full = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 60, video: false));
        Assert.Contains("aevalsrc=0.125*sin(2*PI*1000.000000*(n+16000)/48000)|", full.GraphText, StringComparison.Ordinal);
        Assert.Contains(",asetpts=PTS-STARTPTS,adelay=16000S:all=1", full.GraphText, StringComparison.Ordinal);
        Assert.DoesNotContain("atrim=start_sample", full.GraphText, StringComparison.Ordinal);
        Assert.Empty(full.Warnings);
    }

    [Fact]
    public void TheGeneratorSourcesAreRequiredFilters()
    {
        // Effects batch 5 emits smptehdbars and aevalsrc (color is older); a build without one cannot render.
        var golden = GoldenCases.Get("generators");
        var graph = TimelineCompiler.Compile(golden.Project, golden.Catalog, golden.Request).GraphText;
        foreach (var source in new[] { "color", "smptehdbars", "aevalsrc" })
        {
            Assert.Contains(source + "=", graph, StringComparison.Ordinal);
            Assert.Contains(source, FfmpegCapabilities.RequiredFilters);
        }

        var all = new FfmpegCapabilities("test", 9, 0, FilterScriptSyntax.Slash, [.. FfmpegCapabilities.RequiredFilters], []);
        Assert.True(all.CanRender);
        foreach (var missing in new[] { "smptehdbars", "aevalsrc" })
        {
            var lacking = all with { Filters = all.Filters.Remove(missing) };
            Assert.False(lacking.CanRender);
            Assert.Equal(missing, Assert.Single(lacking.MissingRequiredFilters));
        }
    }

    [Theory]
    [InlineData(-50.0, "20.000000")]
    [InlineData(96000.0, "20000.000000")]
    [InlineData(double.NaN, "1000.000000")]
    public void AnOutOfRangeToneFrequencyIsClampedWithAWarning(double hz, string used)
    {
        // A hand-edited project can hold any toneHz; the render clamps (NaN takes the default) instead of failing.
        var tone = GeneratorClip.Create("c_t", 0, 30, GeneratorKind.Tone) with { ToneHz = hz };
        var project = Project(new SequenceSettings(1920, 1080, FrameRate.Fps30), [], VideoTrack("tr_v1", []), AudioTrack("tr_a1", [tone]));
        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, video: false));

        Assert.Contains("aevalsrc=0.125*sin(2*PI*" + used + "*(n+0)/48000)|", render.GraphText, StringComparison.Ordinal);
        Assert.Contains(render.Warnings, w => w.Contains("tone frequency of clip c_t", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratorEffectsFollowTheMediaOrderAndABadMatteColourIsBlack()
    {
        var matte = GeneratorClip.Create("c_g", 0, 30, GeneratorKind.ColorMatte) with
        {
            Color = "#FF000080",
            Video = VideoProps.Default with { Effects = GoldenCases.AllEffects },
        };
        var project = Project(new SequenceSettings(1920, 1080, FrameRate.Fps30), [], VideoTrack("tr_v1", [matte]));
        var render = TimelineCompiler.Compile(project, TestCatalog.For(project), Export(0, 30, audio: false));

        Assert.Contains("color=c=0x000000:s=1920x1080:r=30,trim=end_frame=30,format=rgb24,", render.GraphText, StringComparison.Ordinal);
        Assert.Contains(render.Warnings, w => w.Contains("matte colour '#FF000080'", StringComparison.Ordinal));
        var graph = render.GraphText;
        var order = new[] { "hflip", "vflip", "colortemperature", "colorbalance", "hue=", "gblur", "unsharp", "vignette", "setsar=1,format=yuva420p", "chromakey", "rotate" }
            .Select(f => graph.IndexOf(f, StringComparison.Ordinal))
            .ToList();
        Assert.All(order, i => Assert.True(i > 0));
        Assert.Equal(order.OrderBy(i => i), order);
    }

    [Fact]
    public void XfadeNamesCoverEveryVideoKind()
    {
        Assert.Equal("fadeblack", VideoEmitter.XfadeName(TransitionKind.DipToBlack));
        Assert.Equal("fadewhite", VideoEmitter.XfadeName(TransitionKind.DipToWhite));
        Assert.Equal("wipetl", VideoEmitter.XfadeName(TransitionKind.WipeTopLeft));
        Assert.Equal("horzopen", VideoEmitter.XfadeName(TransitionKind.HorizontalOpen));
        Assert.Equal("squeezeh", VideoEmitter.XfadeName(TransitionKind.SqueezeHorizontal));

        // Enum driven, so the "_ => fade" default arm cannot hide a forgotten kind: only Crossfade and an audio crossfade
        // that ended up on a video track dissolve, every other kind has its own name, and every name exists in 6.0.
        var names = new Dictionary<TransitionKind, string>();
        foreach (var kind in Enum.GetValues<TransitionKind>())
        {
            var name = VideoEmitter.XfadeName(kind);
            names[kind] = name;
            Assert.True(Allowed60.Contains(name), $"{kind} maps to '{name}', which is not an ffmpeg 6.0 xfade transition.");
            Assert.Equal(kind is TransitionKind.Crossfade or TransitionKind.AudioCrossfade, name == "fade");
        }

        var video = names.Where(n => n.Key != TransitionKind.AudioCrossfade).Select(n => n.Value).ToList();
        Assert.Equal(video.Count, video.Distinct(StringComparer.Ordinal).Count());
    }
}
