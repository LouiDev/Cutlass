using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Exports of the TECH_BRIEF 7.8 timeline shape on real media have exactly the planned frame and sample counts
/// (TECH_BRIEF 11.2, Counts) and carry BT.709 tags.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class ExportCountsTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ExportCountsTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task H264ExportHasExactlyTheFrameCount()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await WorkedExampleShapeAsync(kit, _media, FrameRate.Fps30);

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.H264, "counts_h264", 0, 270, limitPeaks: true));

        ExportKit.AssertCompleted(job);
        Assert.Equal(270, job.Render!.FrameCount);
        Assert.Equal(270, await ExportKit.CountFramesAsync(job.OutputPath));
        var video = await ExportKit.StreamInfoAsync(job.OutputPath, "v");
        Assert.Equal("h264", video["codec_name"]);
        Assert.Equal("bt709", video["color_space"]);
        Assert.Equal("bt709", video["color_transfer"]);
        Assert.Equal("bt709", video["color_primaries"]);
        Assert.Equal("tv", video["color_range"]);
        Assert.Equal(1.0, job.Fraction);
    }

    [FfmpegTheory]
    [InlineData(30, 1)]
    [InlineData(30000, 1001)]
    public async Task ProResExportHasExactFrameAndSampleCounts(long rateNum, long rateDen)
    {
        await using var kit = await ExportKit.CreateAsync();
        var rate = new FrameRate(new Rational(rateNum, rateDen));
        var (project, catalog) = await WorkedExampleShapeAsync(kit, _media, rate);
        const long frames = 263;

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.ProRes, "counts_prores", 7, 7 + frames));

        ExportKit.AssertCompleted(job);
        Assert.Equal(frames, await ExportKit.CountFramesAsync(job.OutputPath));
        var samples = await ExportKit.DecodeAudioAsync(job.OutputPath);
        var expected = TimeMath.SamplesAt(7 + frames, rate) - TimeMath.SamplesAt(7, rate);
        Assert.Equal(expected, samples.Length / 2);
        Assert.Equal(expected, job.Render!.SampleCount);
    }

    [FfmpegFact]
    public async Task WavExportHasExactlyTheSampleCountAt2997()
    {
        await using var kit = await ExportKit.CreateAsync();
        var (project, catalog) = await WorkedExampleShapeAsync(kit, _media, FrameRate.Fps2997);

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Wav, "counts_wav", 0, 97));

        ExportKit.AssertCompleted(job);
        var samples = await ExportKit.DecodeAudioAsync(job.OutputPath);
        Assert.Equal(TimeMath.SamplesAt(97, FrameRate.Fps2997), samples.Length / 2);
        Assert.Equal(155_355, samples.Length / 2);
    }

    /// <summary>
    /// The 7.8 shape on test media at 320x180: two long-GOP and intra oracle clips with a crossfade, a title with fades,
    /// a PiP clip, a crossfaded audio track and an enveloped one.
    /// </summary>
    internal static async Task<(Project Project, TestRenderCatalog Catalog)> WorkedExampleShapeAsync(ExportKit kit, TestMediaFixture fixture, FrameRate rate)
    {
        var lgop = await kit.AssetAsync("m_lgop", fixture.Oracle25LongGop);
        var intra = await kit.AssetAsync("m_intra", fixture.Oracle25Intra);
        var cfr = await kit.AssetAsync("m_cfr", fixture.Cfr25Av);
        var tone = await kit.AssetAsync("m_tone", fixture.Tone);
        var impulse = await kit.AssetAsync("m_imp", fixture.Impulse);
        MediaAsset[] media = [lgop, intra, cfr, tone, impulse];

        var saturated = VideoProps.Default with { Color = ColorAdjust.Neutral with { Saturation = 1.2 } };
        var pip = VideoProps.Default with { Transform = Transform.Identity with { X = 80, Y = -45, Scale = 0.4 } };
        var keys = AudioProps.Default with { Keys = [new VolumeKey(0, -20), new VolumeKey(30, 0)] };
        var title = TitleSpec.Default with { Text = "Day One", Size = 32, OutlineWidth = 2 };
        var project = Project(
            320,
            180,
            rate,
            media,
            Track("tr_v1", TrackKind.Video, [V("c_a", 0, 150, "m_lgop", 2 * ExportKit.F), V("c_b", 150, 120, "m_intra", 10 * ExportKit.F, video: saturated)], [new Transition("x_1", TransitionKind.Crossfade, "c_a", "c_b", 30)]),
            Track("tr_v2", TrackKind.Video, [Title("c_t", 30, 90, title, 15, 15), V("c_c", 180, 90, "m_cfr", 0, video: pip)]),
            Track("tr_a1", TrackKind.Audio, [A("c_aa", 0, 150, "m_tone", 2 * ExportKit.F), A("c_ba", 150, 120, "m_imp", 10 * ExportKit.F)], [new Transition("x_2", TransitionKind.AudioCrossfade, "c_aa", "c_ba", 30)]),
            Track("tr_a2", TrackKind.Audio, [A("c_ca", 180, 90, "m_cfr", 0, audio: keys)]));
        return (project, await kit.CatalogAsync(media));
    }
}
