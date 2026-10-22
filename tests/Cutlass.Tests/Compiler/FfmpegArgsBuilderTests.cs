using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Time;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>FfmpegArgsBuilder: the exact export command of every preset (TECH_BRIEF 9), inputs, graphs and previews.</summary>
[UseCulture("de-DE")]
public sealed class FfmpegArgsBuilderTests
{
    private const string Output = "D:/Out/final.partial.ext";

    private static readonly string[] Head = ["-hide_banner", "-nostats", "-progress", "pipe:1", "-v", "warning", "-y"];

    private static readonly string[] CommonVideo =
        ["-fps_mode", "cfr", "-r", "30", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709", "-color_range", "tv"];

    public static TheoryData<string, string, string> PresetCommands() => new()
    {
        { "h264", "High", "-map [vout] -map [aout] -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -profile:v high {video} -c:a aac -b:a 192k -ar 48000 -ac 2 -movflags +faststart" },
        { "h264", "Draft", "-map [vout] -map [aout] -c:v libx264 -preset medium -crf 28 -pix_fmt yuv420p -profile:v high {video} -c:a aac -b:a 192k -ar 48000 -ac 2 -movflags +faststart" },
        { "h265", "Good", "-map [vout] -map [aout] -c:v libx265 -preset medium -crf 26 -pix_fmt yuv420p -tag:v hvc1 {video} -c:a aac -b:a 192k -ar 48000 -ac 2 -movflags +faststart" },
        { "vp9", "Best", "-map [vout] -map [aout] -c:v libvpx-vp9 -crf 26 -b:v 0 -row-mt 1 -deadline good -cpu-used 2 -pix_fmt yuv420p {video} -c:a libopus -b:a 160k -ar 48000 -ac 2" },
        { "av1", "High", "-map [vout] -map [aout] -c:v libsvtav1 -preset 8 -crf 32 -pix_fmt yuv420p {video} -c:a aac -b:a 192k -ar 48000 -ac 2 -movflags +faststart" },
        { "prores", "Draft", "-map [vout] -map [aout] -c:v prores_ks -profile:v 3 -vendor apl0 -pix_fmt yuv422p10le {video} -c:a pcm_s16le -ar 48000 -ac 2" },
        { "gif", "Best", "-map [vout] -loop 0" },
        { "mp3", "Good", "-map [aout] -vn -c:a libmp3lame -q:a 4 -ar 48000 -ac 2" },
        { "mp3", "Best", "-map [aout] -vn -c:a libmp3lame -q:a 0 -ar 48000 -ac 2" },
        { "aac", "High", "-map [aout] -vn -c:a aac -b:a 256k -ar 48000 -ac 2" },
        { "wav", "High", "-map [aout] -vn -c:a pcm_s16le -ar 48000 -ac 2" },
        { "flac", "High", "-map [aout] -vn -c:a flac -ar 48000 -ac 2" },
    };

    [Theory]
    [MemberData(nameof(PresetCommands))]
    public void ExportCommandPerPreset(string presetId, string quality, string expectedTail)
    {
        var preset = ExportPresets.Find(presetId)!;
        var render = Compile(preset);
        var encoding = preset.Resolve(WorkedExampleTests.MasterCapabilities())!;

        var args = FfmpegArgsBuilder.Export(
            render, encoding, Enum.Parse<ExportQuality>(quality), FrameRate.Fps30, 1920, 1080, ["-/filter_complex", "g_000000000000.ffg"], Output);

        List<string> expected = [.. Head, "-ss", "0.000000", "-t", "1.100000", "-i", "D:/Clips/a.mp4", "-/filter_complex", "g_000000000000.ffg"];
        foreach (var token in expectedTail.Split(' '))
        {
            if (token == "{video}")
            {
                expected.AddRange(CommonVideo);
            }
            else
            {
                expected.Add(token);
            }
        }

        expected.Add(Output);
        Assert.Equal(expected, args);
        Assert.DoesNotContain("-nostdin", args);
    }

    [Fact]
    public void FallbackEncodersAreUsedWhenThePrimaryIsMissing()
    {
        var caps = WorkedExampleTests.MasterCapabilities(FilterScriptSyntax.Slash, "libx264", "libsvtav1", "libopus");
        var h264 = ExportPresets.H264.Resolve(caps)!;
        var av1 = ExportPresets.Av1.Resolve(caps)!;
        var vp9 = ExportPresets.Vp9.Resolve(caps)!;
        var render = Compile(ExportPresets.H264);

        var h264Args = FfmpegArgsBuilder.Export(render, h264, ExportQuality.Good, FrameRate.Fps30, 1280, 720, ["-filter_complex", "x"], Output);
        var av1Args = FfmpegArgsBuilder.Export(render, av1, ExportQuality.Draft, FrameRate.Fps30, 1280, 720, ["-filter_complex", "x"], Output);
        var vp9Args = FfmpegArgsBuilder.Export(render, vp9, ExportQuality.Draft, FrameRate.Fps30, 1280, 720, ["-filter_complex", "x"], Output);

        // 8 Mb/s at 1080p scaled by 1280x720 / 1920x1080.
        Assert.Equal("-c:v libopenh264 -b:v 3555555 -pix_fmt yuv420p -profile:v high", string.Join(' ', h264Args.SkipWhile(a => a != "-c:v").Take(8)));
        Assert.Equal("-c:v libaom-av1 -cpu-used 6 -row-mt 1 -crf 42 -b:v 0 -pix_fmt yuv420p", string.Join(' ', av1Args.SkipWhile(a => a != "-c:v").Take(12)));
        Assert.Equal("-c:a libvorbis -q:a 5", string.Join(' ', vp9Args.SkipWhile(a => a != "-c:a").Take(4)));
    }

    [Theory]
    [InlineData(ExportQuality.Draft, 1920, 1080, 4_000_000)]
    [InlineData(ExportQuality.Good, 1920, 1080, 8_000_000)]
    [InlineData(ExportQuality.High, 1920, 1080, 12_000_000)]
    [InlineData(ExportQuality.Best, 1920, 1080, 20_000_000)]
    [InlineData(ExportQuality.Best, 3840, 2160, 80_000_000)]
    [InlineData(ExportQuality.Draft, 2, 2, 100_000)]
    public void OpenH264BitrateScalesWithPixels(ExportQuality quality, int width, int height, long expected)
    {
        Assert.Equal(expected, FfmpegArgsBuilder.OpenH264Bitrate(quality, width, height));
    }

    [Fact]
    public void InputsCarrySeekSpanAndStillLooping()
    {
        Assert.Equal(
            ["-ss", "1.500000", "-t", "2.250000", "-i", "D:/a b/it's.mp4"],
            FfmpegArgsBuilder.Input(new InputSpec("D:/a b/it's.mp4", 1_500_000, 2_250_000, false, null)));
        Assert.Equal(
            ["-loop", "1", "-framerate", "30000/1001", "-t", "5.100000", "-i", "/home/u/logo.png"],
            FfmpegArgsBuilder.Input(new InputSpec("/home/u/logo.png", null, 5_100_000, true, new Rational(30000, 1001))));
    }

    [Fact]
    public void GraphOptionFollowsTheScriptSyntax()
    {
        Assert.Equal(["-/filter_complex", "g_1.ffg"], FfmpegArgsBuilder.Graph("x", "g_1.ffg", FilterScriptSyntax.Slash));
        Assert.Equal(["-filter_complex_script", "g_1.ffg"], FfmpegArgsBuilder.Graph("x", "g_1.ffg", FilterScriptSyntax.LegacyScript));
        Assert.Equal(["-filter_complex", "x"], FfmpegArgsBuilder.Graph("x", "g_1.ffg", FilterScriptSyntax.InlineOnly));
        Assert.Equal(["-filter_complex", "x"], FfmpegArgsBuilder.Graph("x", null, FilterScriptSyntax.Slash));
        Assert.Throws<ArgumentException>(() => FfmpegArgsBuilder.Graph("x", "C:/work/g_1.ffg", FilterScriptSyntax.Slash));
        Assert.Throws<InvalidOperationException>(() => FfmpegArgsBuilder.Graph(new string('a', 30_001), null, FilterScriptSyntax.InlineOnly));
        Assert.Single(FfmpegArgsBuilder.Graph(new string('a', 30_000), null, FilterScriptSyntax.InlineOnly).Skip(1));
    }

    [Fact]
    public void ScriptFilesForExportsAndLargePreviewsOnly()
    {
        var small = new string('a', 8 * 1024);
        var large = small + "b";

        Assert.True(FfmpegArgsBuilder.UseScriptFile(RenderPurpose.Export, "x", FilterScriptSyntax.Slash));
        Assert.True(FfmpegArgsBuilder.UseScriptFile(RenderPurpose.Export, "x", FilterScriptSyntax.LegacyScript));
        Assert.False(FfmpegArgsBuilder.UseScriptFile(RenderPurpose.Export, "x", FilterScriptSyntax.InlineOnly));
        Assert.False(FfmpegArgsBuilder.UseScriptFile(RenderPurpose.PreviewFrame, small, FilterScriptSyntax.Slash));
        Assert.True(FfmpegArgsBuilder.UseScriptFile(RenderPurpose.PreviewVideo, large, FilterScriptSyntax.Slash));
        Assert.True(FfmpegArgsBuilder.UseScriptFile(RenderPurpose.PreviewFrame, new string('\u00e4', 4097), FilterScriptSyntax.Slash));
    }

    [Fact]
    public void PreviewCommandsPipeRawFrames()
    {
        var c = GoldenCases.Get("window_mid_crossfade");
        var render = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);

        var video = FfmpegArgsBuilder.RawVideo(render, ["-filter_complex", "G"], 1);

        Assert.Equal(
            "-hide_banner -nostdin -v error -ss 6.500000 -t 1.100000 -i D:/Clips/a.mp4 -ss 9.500000 -t 1.100000 -i D:/Clips/b.mp4 " +
            "-filter_complex G -map [vout] -frames:v 1 -fps_mode passthrough -f rawvideo -pix_fmt bgra pipe:1",
            string.Join(' ', video));
        Assert.Throws<ArgumentException>(() => FfmpegArgsBuilder.RawAudio(render, ["-filter_complex", "G"]));

        var pcm = GoldenCases.Get("per_track_audio");
        var audio = FfmpegArgsBuilder.RawAudio(TimelineCompiler.Compile(pcm.Project, pcm.Catalog, pcm.Request), ["-filter_complex", "G"]);
        Assert.Equal(["-map", "[aout]", "-f", "s16le", "-c:a", "pcm_s16le", "pipe:1"], audio.Skip(audio.Count - 7));
    }

    private static CompiledRender Compile(ExportPreset preset)
    {
        var project = CompilerKit.Project(
            new Cutlass.Core.Model.SequenceSettings(1920, 1080, FrameRate.Fps30),
            [CompilerKit.VideoAsset("m_a", "D:/Clips/a.mp4")],
            CompilerKit.VideoTrack("tr_v1", [CompilerKit.V("c_v", 0, 30, "m_a")]),
            CompilerKit.AudioTrack("tr_a1", [CompilerKit.A("c_a", 0, 30, "m_a")]));
        var request = new RenderRequest(0, 30, 1920, 1080, RenderPurpose.Export, preset.HasVideo, preset.HasAudio, false, AudioLayout.Mixed, 1, preset.Tail, true);
        return TimelineCompiler.Compile(project, TestCatalog.For(project), request);
    }
}
