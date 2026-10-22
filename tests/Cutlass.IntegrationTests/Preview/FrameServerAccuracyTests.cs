using System.Globalization;
using System.Text;
using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Export;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Preview;
using static Cutlass.IntegrationTests.Export.TimelineKit;

// Not ".Preview": that namespace would hide TimelineKit.Preview(...) in the Export tests, which import it statically.
namespace Cutlass.IntegrationTests.PreviewTests;

/// <summary>
/// Scrubbing through the <see cref="FrameServer"/> is frame exact (TECH_BRIEF 11.2): at 50 random frames of every
/// oracle, imported with <c>MediaImporter</c> (so seeks use its frame index), at 50%, 100% and 200% speed, each
/// presented frame shows <c>expected(k)</c>, and black outside the clip.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class FrameServerAccuracyTests
{
    private const long ClipStart = 5;
    private const long ClipDuration = 240;
    private const int Samples = 50;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(60);

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public FrameServerAccuracyTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegTheory]
    [InlineData("intra", 50, 30, 1)]
    [InlineData("intra", 100, 30000, 1001)]
    [InlineData("intra", 200, 30, 1)]
    [InlineData("lgop", 50, 30000, 1001)]
    [InlineData("lgop", 100, 30, 1)]
    [InlineData("lgop", 200, 30000, 1001)]
    [InlineData("ntsc", 50, 30, 1)]
    [InlineData("ntsc", 100, 30000, 1001)]
    [InlineData("ntsc", 200, 30, 1)]
    [InlineData("vfr", 50, 30000, 1001)]
    [InlineData("vfr", 100, 30, 1)]
    [InlineData("vfr", 200, 30000, 1001)]
    public async Task ScrubbedFramesShowTheExpectedSourceFrame(string source, int speed, long rateNum, long rateDen)
    {
        await using var kit = await PreviewKit.CreateAsync();
        var imported = await kit.Importer.ImportAsync(PathOf(source), CancellationToken.None);
        var asset = imported.Asset;
        var index = kit.Catalog.Get(asset.Id)!.Original.Index;
        Assert.NotNull(index);

        var rate = new FrameRate(new Rational(rateNum, rateDen));
        var clip = V("c_o", ClipStart, ClipDuration, asset.Id, Micros(1_234_567), speed);
        var project = Project(320, 180, rate, [asset], Track("tr_v1", TrackKind.Video, [clip]));

        var sink = new RecordingSink();
        var server = kit.Server(sink);
        var failures = new StringBuilder();
        server.RenderFailed += (id, message) => { lock (failures) { failures.Append(CultureInfo.InvariantCulture, $"request {id} failed: {message}; "); } };

        var random = new Random(HashCode.Combine(source.Length, speed, rateDen) + 1337);
        var size = FrameServer.FitSize(project.Sequence.Settings, 320, 180);
        Assert.Equal(new PreviewSize(320, 180), size);
        var mismatches = new StringBuilder();
        for (var i = 1; i <= Samples; i++)
        {
            var frame = random.NextInt64(0, ClipStart + ClipDuration + 5);
            server.Request(new FrameRequest(project, frame, size, false, i));
            var presented = await sink.WaitAsync(i, FrameTimeout);
            Assert.Equal(frame, presented.TimelineFrame);
            Assert.Equal(320 * 4, presented.Stride);

            var actual = OracleReader.DecodeBgra(presented.Pixels, 320, 180, presented.Stride);
            var expected = frame < ClipStart || frame >= ClipStart + ClipDuration
                ? 0
                : TestMediaFixture.ExpectedOracleValue(SourceFrame(source, TimeMath.SourceFlicksAt(clip, frame, rate), index));
            if (actual != expected)
            {
                mismatches.Append(CultureInfo.InvariantCulture, $"frame {frame}: expected {expected}, got {actual}; ");
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
        Assert.True(mismatches.Length == 0, mismatches.ToString());
    }

    private static long SourceFrame(string source, long sourceFlicks, FrameIndex? index) => source switch
    {
        // Frame k of a 25 fps oracle sits exactly at k/25 s; sample-and-hold shows floor(s * 25).
        "intra" or "lgop" => (long)TimeMath.FloorDiv((Int128)sourceFlicks * 25, TimeMath.FlicksPerSecond),

        // The 30000/1001 Matroska oracle (1 ms ticks) and the VFR oracle: the largest stored pts at or before s.
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
