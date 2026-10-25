using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// The bit-block oracle proves frame-exact trims and placement (TECH_BRIEF 7.2 and 11.2): every rendered frame shows
/// the source frame with the largest pts at or before <c>s(f)</c>, at 30 and 30000/1001 timelines, at 50%, 100% and
/// 200% speed, with an off-grid source in point, with and without a frame index, and black outside the clip.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class OracleAccuracyTests
{
    private const long ClipStart = 7;
    private const long ClipDuration = 40;
    private const long WindowEnd = 55;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public OracleAccuracyTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegTheory]
    [InlineData("intra", 30, 1, 50, true)]
    [InlineData("intra", 30, 1, 100, true)]
    [InlineData("intra", 30, 1, 200, true)]
    [InlineData("intra", 30000, 1001, 50, true)]
    [InlineData("intra", 30000, 1001, 100, true)]
    [InlineData("intra", 30000, 1001, 200, true)]
    [InlineData("lgop", 30, 1, 50, true)]
    [InlineData("lgop", 30, 1, 100, true)]
    [InlineData("lgop", 30, 1, 200, true)]
    [InlineData("lgop", 30000, 1001, 50, true)]
    [InlineData("lgop", 30000, 1001, 100, true)]
    [InlineData("lgop", 30000, 1001, 200, true)]
    [InlineData("ntsc", 30, 1, 100, true)]
    [InlineData("ntsc", 30000, 1001, 100, true)]
    [InlineData("ntsc", 30000, 1001, 200, true)]
    [InlineData("intra", 30, 1, 100, false)]
    [InlineData("lgop", 30000, 1001, 50, false)]
    [InlineData("intra", 30000, 1001, 200, false)]
    [InlineData("vfr", 30, 1, 100, true)]
    [InlineData("vfr", 30000, 1001, 200, true)]
    public async Task EveryFrameShowsTheExpectedSourceFrame(string source, long rateNum, long rateDen, int speed, bool withIndex)
    {
        await using var kit = await ExportKit.CreateAsync();
        var rate = new FrameRate(new Rational(rateNum, rateDen));
        var asset = await kit.AssetAsync("m_o", PathOf(source));
        var catalog = await kit.CatalogAsync([asset], withIndex);
        var clip = V("c_o", ClipStart, ClipDuration, "m_o", Micros(1_234_567), speed);
        var project = Project(320, 180, rate, [asset], Track("tr_v1", TrackKind.Video, [clip]));

        var frames = await kit.RenderBgraAsync(project, catalog, Preview(0, WindowEnd, 320, 180));

        var index = catalog.Get("m_o")!.Original.Index;
        var failures = new StringBuilder();
        for (long f = 0; f < WindowEnd; f++)
        {
            var actual = OracleReader.DecodeBgra(frames.AsSpan((int)(f * 320 * 180 * 4), 320 * 180 * 4), 320, 180);
            var expected = f < ClipStart || f >= ClipStart + ClipDuration
                ? 0
                : TestMediaFixture.ExpectedOracleValue(SourceFrame(source, TimeMath.SourceFlicksAt(clip, f, rate), index));
            if (actual != expected)
            {
                failures.Append(System.Globalization.CultureInfo.InvariantCulture, $"frame {f}: expected {expected}, got {actual}; ");
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
    }

    [FfmpegTheory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(254)]
    public async Task AFrozenClipShowsItsSourceFrameOnEveryOutputFrame(long k)
    {
        // Effects batch 4: source frame k of the 25 fps oracle starts at k * 40 ms. The frozen clip holds it from
        // frame 7 for 30 frames, in a full render and in a window that starts inside the clip (range independence).
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_o", _media.Oracle25Intra);
        var catalog = await kit.CatalogAsync([asset]);
        var clip = V("c_f", ClipStart, 30, "m_o", Micros(k * 40_000)) with { Freeze = true };
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_v1", TrackKind.Video, [clip]));

        var frames = await kit.RenderBgraAsync(project, catalog, Preview(0, 40, 320, 180));
        for (long f = 0; f < 40; f++)
        {
            var actual = OracleReader.DecodeBgra(frames.AsSpan((int)(f * 320 * 180 * 4), 320 * 180 * 4), 320, 180);
            var expected = f < ClipStart || f >= ClipStart + 30 ? 0 : TestMediaFixture.ExpectedOracleValue(k);
            Assert.True(expected == actual, $"frame {f}: expected {expected}, got {actual}");
        }

        var window = await kit.RenderBgraAsync(project, catalog, Preview(20, 37, 320, 180));
        for (long f = 0; f < 17; f++)
        {
            var actual = OracleReader.DecodeBgra(window.AsSpan((int)(f * 320 * 180 * 4), 320 * 180 * 4), 320, 180);
            Assert.True(TestMediaFixture.ExpectedOracleValue(k) == actual, $"window frame {20 + f}: got {actual}");
        }
    }

    [FfmpegFact]
    public async Task H264ExportKeepsTheOracleFramesInPlace()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_o", _media.Oracle25LongGop);
        var catalog = await kit.CatalogAsync([asset]);
        var clip = V("c_o", ClipStart, ClipDuration, "m_o", Micros(3_210_987), 100);
        var project = Project(320, 180, FrameRate.Fps2997, [asset], Track("tr_v1", TrackKind.Video, [clip]));

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.H264, "oracle_h264", 0, WindowEnd, quality: ExportQuality.Best));

        ExportKit.AssertCompleted(job);
        var gray = await ExportKit.DecodeGrayAsync(job.OutputPath);
        Assert.Equal(WindowEnd * 320 * 180, gray.Length);
        for (long f = 0; f < WindowEnd; f++)
        {
            var actual = OracleReader.DecodeGray(gray.AsSpan((int)(f * 320 * 180), 320 * 180), 320, 180);
            var expected = f < ClipStart || f >= ClipStart + ClipDuration
                ? 0
                : TestMediaFixture.ExpectedOracleValue(SourceFrame("lgop", TimeMath.SourceFlicksAt(clip, f, FrameRate.Fps2997), null));
            Assert.True(expected == actual, $"frame {f}: expected {expected}, got {actual}");
        }
    }

    private static long SourceFrame(string source, long sourceFlicks, FrameIndex? index) => source switch
    {
        // Frame k of a 25 fps oracle sits exactly at k/25 s; sample-and-hold shows floor(s * 25).
        "intra" or "lgop" => (long)TimeMath.FloorDiv((Int128)sourceFlicks * 25, TimeMath.FlicksPerSecond),

        // Matroska stores the 30000/1001 oracle in 1 ms ticks and the VFR oracle irregularly: sample-and-hold shows
        // the largest stored pts at or before s, which the packet index lists (both files start at 0).
        _ => index!.FloorOrdinal(sourceFlicks),
    };

    private string PathOf(string source) => source switch
    {
        "intra" => _media.Oracle25Intra,
        "lgop" => _media.Oracle25LongGop,
        "ntsc" => _media.Oracle2997,
        _ => _media.OracleVfr,
    };
}
