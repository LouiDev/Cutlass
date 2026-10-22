using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Services;

namespace Cutlass.IntegrationTests.Media;

/// <summary>ProbeService against real ffprobe on the generated media (TECH_BRIEF 6.3).</summary>
[Collection(TestMediaCollection.Name)]
public sealed class ProbeServiceTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ProbeServiceTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task Cfr25ClipReportsEveryField()
    {
        await using var h = await MediaHarness.CreateAsync();
        var probe = new ProbeService(h.Runner, h.Toolset, h.Work);

        var result = await probe.ProbeAsync(_media.Cfr25Av, CancellationToken.None);
        var info = result.Info;

        Assert.Contains("\"streams\"", result.Json, StringComparison.Ordinal);
        Assert.Contains("mp4", info.FormatName, StringComparison.Ordinal);
        Assert.Equal(0, info.FormatStartMicros);
        Assert.InRange(info.DurationMicros, 3_990_000, 4_100_000);
        Assert.False(info.IsStill);

        var v = Assert.IsType<Cutlass.Core.Model.VideoStreamInfo>(info.Video);
        Assert.Equal(0, v.StreamIndex);
        Assert.Equal("mpeg4", v.Codec);
        Assert.Equal(320, v.Width);
        Assert.Equal(180, v.Height);
        Assert.Equal(320, v.DisplayWidth);
        Assert.Equal(180, v.DisplayHeight);
        Assert.Equal(0, v.Rotation);
        Assert.Equal(Rational.One, v.Sar);
        Assert.Equal(new Rational(25, 1), v.RFrameRate);
        Assert.Equal(new Rational(25, 1), v.AvgFrameRate);
        Assert.True(v.TimeBase.IsPositive);
        Assert.Equal(0, v.StartPts);
        Assert.Equal("yuv420p", v.PixFmt);
        Assert.False(v.IsVfr);
        Assert.False(v.IsHdr);
        Assert.True(v.BitRate > 0);

        var a = Assert.IsType<Cutlass.Core.Model.AudioStreamInfo>(info.Audio);
        Assert.Equal(1, a.StreamIndex);
        Assert.Equal("aac", a.Codec);
        Assert.Equal(48_000, a.SampleRate);
        Assert.Equal(2, a.Channels);
    }

    [FfmpegFact]
    public async Task Ntsc2997ClipReportsRateSampleRateAndChannels()
    {
        await using var h = await MediaHarness.CreateAsync();
        var info = (await new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(_media.Cfr2997Av, CancellationToken.None)).Info;

        Assert.Contains("matroska", info.FormatName, StringComparison.Ordinal);
        Assert.Equal(new Rational(30000, 1001), info.Video!.RFrameRate);
        Assert.Equal(new Rational(30000, 1001), info.Video.AvgFrameRate);
        Assert.Equal(new Rational(1, 1000), info.Video.TimeBase);
        Assert.False(info.Video.IsVfr);
        Assert.Equal("pcm_s16le", info.Audio!.Codec);
        Assert.Equal(44_100, info.Audio.SampleRate);
        Assert.Equal(1, info.Audio.Channels);
        Assert.InRange(info.DurationMicros, 2_990_000, 3_100_000);
    }

    [FfmpegFact]
    public async Task NonZeroStartTimeBecomesFormatStartMicros()
    {
        await using var h = await MediaHarness.CreateAsync();
        var info = (await new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(_media.OffsetStart, CancellationToken.None)).Info;

        Assert.Equal(2_500_000, info.FormatStartMicros);
        Assert.InRange(info.DurationMicros, 1_990_000, 2_100_000);
        Assert.NotNull(info.Video);
        Assert.NotNull(info.Audio);
    }

    [FfmpegFact]
    public async Task MixedRateClipIsVfr()
    {
        await using var h = await MediaHarness.CreateAsync();
        var info = (await new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(_media.VfrMixed, CancellationToken.None)).Info;

        Assert.True(info.Video!.IsVfr);
        Assert.Equal(new Rational(30, 1), info.Video.RFrameRate);
        Assert.InRange(info.Video.AvgFrameRate.ToDouble(), 12.0, 14.0);
        Assert.Null(info.Audio);
    }

    [FfmpegFact]
    public async Task OracleVfrHasEqualRatesSoItIsNotFlagged()
    {
        // The oracle only jitters by up to 20 ms; r_frame_rate and avg_frame_rate both stay 25 (TECH_BRIEF 6.3 rule).
        await using var h = await MediaHarness.CreateAsync();
        var info = (await new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(_media.OracleVfr, CancellationToken.None)).Info;

        Assert.False(info.Video!.IsVfr);
        Assert.Equal(new Rational(1, 1000), info.Video.TimeBase);
    }

    [FfmpegFact]
    public async Task DisplayMatrixRotationSwapsDisplaySize()
    {
        await using var h = await MediaHarness.CreateAsync();
        var info = (await new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(_media.Rotated90, CancellationToken.None)).Info;

        // -display_rotation 90 is counter-clockwise; Cutlass stores the clockwise angle (ProbeParser.NormalizeRotation).
        Assert.Equal(270, info.Video!.Rotation);
        Assert.Equal(320, info.Video.Width);
        Assert.Equal(180, info.Video.Height);
        Assert.Equal(180, info.Video.DisplayWidth);
        Assert.Equal(320, info.Video.DisplayHeight);
    }

    [FfmpegFact]
    public async Task AudioOnlyAndVideoOnlyFiles()
    {
        await using var h = await MediaHarness.CreateAsync();
        var probe = new ProbeService(h.Runner, h.Toolset, h.Work);

        var tone = (await probe.ProbeAsync(_media.Tone, CancellationToken.None)).Info;
        Assert.Null(tone.Video);
        Assert.Equal("pcm_s16le", tone.Audio!.Codec);
        Assert.Equal(48_000, tone.Audio.SampleRate);
        Assert.Equal(1, tone.Audio.Channels);
        Assert.Equal(10_000_000, tone.DurationMicros);
        Assert.False(tone.IsStill);

        var intra = (await probe.ProbeAsync(_media.Oracle25Intra, CancellationToken.None)).Info;
        Assert.Null(intra.Audio);
        Assert.Equal("ffv1", intra.Video!.Codec);
        Assert.Equal(20_000_000, intra.DurationMicros);
    }

    [FfmpegFact]
    public async Task PngIsAStill()
    {
        await using var h = await MediaHarness.CreateAsync();
        var info = (await new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(_media.RedPng, CancellationToken.None)).Info;

        Assert.True(info.IsStill);
        Assert.Equal("png", info.Video!.Codec);
        Assert.Equal(320, info.Video.DisplayWidth);
        Assert.Equal(240, info.Video.DisplayHeight);
    }

    [FfmpegFact]
    public async Task OddPathProbes()
    {
        await using var h = await MediaHarness.CreateAsync();
        var info = (await new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(_media.OddPathClip, CancellationToken.None)).Info;

        Assert.Equal(320, info.Video!.Width);
        Assert.Equal(48_000, info.Audio!.SampleRate);
    }

    [FfmpegFact]
    public async Task ResultsAreCachedByPathSizeAndMtime()
    {
        await using var h = await MediaHarness.CreateAsync();
        var probe = new ProbeService(h.Runner, h.Toolset, h.Work);
        var copy = Path.Combine(h.Root, "copy.mkv");
        File.Copy(_media.Cfr2997Av, copy);

        var first = await probe.ProbeAsync(copy, CancellationToken.None);
        var second = await probe.ProbeAsync(copy, CancellationToken.None);
        Assert.Same(first, second);

        // A different file under the same path (new size and mtime) is probed again.
        File.Copy(_media.Tone, copy, overwrite: true);
        File.SetLastWriteTimeUtc(copy, DateTime.UtcNow.AddMinutes(1));
        var third = await probe.ProbeAsync(copy, CancellationToken.None);
        Assert.NotSame(first, third);
        Assert.Null(third.Info.Video);
    }

    [FfmpegFact]
    public async Task NonMediaFileRaisesFfmpegExceptionWithStderr()
    {
        await using var h = await MediaHarness.CreateAsync();
        var bogus = Path.Combine(h.Root, "not media.mp4");
        await File.WriteAllTextAsync(bogus, "this is not a video");

        var ex = await Assert.ThrowsAsync<FfmpegException>(
            () => new ProbeService(h.Runner, h.Toolset, h.Work).ProbeAsync(bogus, CancellationToken.None));

        Assert.NotEqual(0, ex.ExitCode);
        Assert.NotEmpty(ex.StderrTail);
        Assert.Contains("ffprobe", ex.DisplayCommand, StringComparison.Ordinal);
    }
}
