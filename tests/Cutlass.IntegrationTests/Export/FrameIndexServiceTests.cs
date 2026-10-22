using System.Globalization;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>FrameIndexService against real ffprobe packet passes (TECH_BRIEF 6.4).</summary>
[Collection(TestMediaCollection.Name)]
public sealed class FrameIndexServiceTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public FrameIndexServiceTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task IntraOracleIndexHasEveryFrame()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_o", _media.Oracle25Intra);

        var index = await kit.Index.GetAsync(asset, CancellationToken.None);

        Assert.NotNull(index);
        Assert.Equal(TestMediaFixture.Oracle25FrameCount, index.Count);
        Assert.Equal(0, index.FloorOrdinal(0));
        Assert.Equal(25, index.FloorOrdinal(TimeMath.FlicksPerSecond));
        Assert.Equal(24, index.FloorOrdinal(TimeMath.FlicksPerSecond - 1));
    }

    [FfmpegFact]
    public async Task LongGopIndexIsSortedInPresentationOrder()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_o", _media.Oracle25LongGop);

        var index = await kit.Index.GetAsync(asset, CancellationToken.None);

        Assert.NotNull(index);
        Assert.Equal(TestMediaFixture.Oracle25FrameCount, index.Count);
        for (var i = 1; i < index.Count; i++)
        {
            Assert.True(index[i] > index[i - 1]);
        }
    }

    [FfmpegFact]
    public async Task VfrOracleIndexKeepsTheIrregularTimestamps()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_vfr", _media.OracleVfr);

        var index = await kit.Index.GetAsync(asset, CancellationToken.None);

        Assert.NotNull(index);
        Assert.Equal(new Rational(1, 1000), index.TimeBase);
        Assert.Equal(TestMediaFixture.Oracle25FrameCount, index.Count);
        for (var n = 0; n < index.Count; n++)
        {
            // The fixture's setpts truncates its double result, so a timestamp may sit 1 ms below the ideal value.
            var ideal = (40L * n) + (10L * (n % 3));
            Assert.InRange(index[n], ideal - 1, ideal);
        }
    }

    [FfmpegFact]
    public async Task StillsHaveNoIndexAndResultsAreCached()
    {
        await using var kit = await ExportKit.CreateAsync();
        var still = await kit.AssetAsync("m_png", _media.RedPng);
        var clip = await kit.AssetAsync("m_cfr", _media.Cfr25Av);

        Assert.Null(await kit.Index.GetAsync(still, CancellationToken.None));
        var first = await kit.Index.GetAsync(clip, CancellationToken.None);
        var second = await kit.Index.GetAsync(clip, CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(1, kit.Index.CachedCount);
        kit.Index.Invalidate(clip.Path);
        Assert.Equal(0, kit.Index.CachedCount);
    }

    [FfmpegFact]
    public async Task AviWithBFramesFallsBackToCfrAndShowsTheDecodedFrames()
    {
        // In AVI, mpeg4 with two B-frames has no pts on a third of its packets; their dts ran the index one frame
        // ahead of the decoder (TECH_BRIEF 6.4). Without an index the CFR fallback renders what the decoder shows.
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_avi", _media.Oracle25AviBFrames);

        Assert.Null(await kit.Index.GetAsync(asset, CancellationToken.None));

        var decoded = await DecodedTimestampsAsync(_media.Oracle25AviBFrames);
        var catalog = await kit.CatalogAsync([asset]);
        var clip = V("c_o", 0, 40, "m_avi", Micros(400_000));
        var project = Project(320, 180, FrameRate.Fps25, [asset], Track("tr_v1", TrackKind.Video, [clip]));
        var frames = await kit.RenderBgraAsync(project, catalog, Preview(0, 40, 320, 180));

        for (long f = 0; f < 40; f++)
        {
            // Sample-and-hold on the decoded timestamps (1/25 ticks): the last decoded frame at or before s.
            var s = 10 + f;
            var expected = Math.Max(0, decoded.FindLastIndex(t => t <= s));
            var actual = OracleReader.DecodeBgra(frames.AsSpan((int)(f * 320 * 180 * 4), 320 * 180 * 4), 320, 180);
            Assert.True(TestMediaFixture.ExpectedOracleValue(expected) == actual, $"frame {f}: expected {expected}, got {actual}");
        }
    }

    // best_effort_timestamp of every decoded frame in 1/25 ticks; a missing one continues the previous.
    private static async Task<List<long>> DecodedTimestampsAsync(string file)
    {
        var result = await TestProcess.RunAsync(
            TestFfmpeg.FfprobePath,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "frame=best_effort_timestamp", "-of", "csv=p=0", file],
            Path.GetTempPath(),
            TimeSpan.FromMinutes(1));
        Assert.Equal(0, result.ExitCode);
        var list = new List<long>();
        foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            list.Add(long.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : list[^1] + 1);
        }

        return list;
    }
}
