using System.Globalization;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Speed-changed audio is placed where it belongs (TECH_BRIEF 7.3): the atempo pre-roll and the calibrated lead cut put
/// every tone burst of <see cref="TestMediaFixture.Bursts"/> within 1 ms of <c>(source position - source in) * 100 /
/// speed</c> on average, in an export and across playback window seams.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class AtempoAlignmentTests
{
    /// <summary>The allowed mean error: 1 ms.</summary>
    private const double MeanTolerance = 48;

    /// <summary>The allowed error of one burst: WSOLA moves single bursts by a few milliseconds.</summary>
    private const double BurstTolerance = 288;

    private const long SourceInMicros = 500_000;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public AtempoAlignmentTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegTheory]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(150)]
    [InlineData(200)]
    [InlineData(300)]
    public async Task BurstsLandWithinOneMillisecondInAnExport(int speed)
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_b", _media.Bursts);
        var catalog = await kit.CatalogAsync([asset]);
        var frames = ClipFrames(speed);
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_a1", TrackKind.Audio, [A("c_a", 0, frames, "m_b", Micros(SourceInMicros), speed)]));

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Wav, "atempo" + speed.ToString(CultureInfo.InvariantCulture), 0, frames));

        ExportKit.AssertCompleted(job);
        var samples = await ExportKit.DecodeAudioAsync(job.OutputPath);
        AssertAligned(Left(samples, 2), speed);
    }

    [FfmpegTheory]
    [InlineData(50)]
    [InlineData(200)]
    public async Task PlaybackWindowsJoinWithoutSkippingSourceAudio(int speed)
    {
        // Playback restarts atempo in every window; the pre-roll makes consecutive windows continue seamlessly.
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_b", _media.Bursts);
        var catalog = await kit.CatalogAsync([asset]);
        var frames = ClipFrames(speed);
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_a1", TrackKind.Audio, [A("c_a", 0, frames, "m_b", Micros(SourceInMicros), speed)]));

        var joined = new List<float>();
        const long WindowFrames = 37;
        for (long start = 0; start < frames; start += WindowFrames)
        {
            var end = Math.Min(frames, start + WindowFrames);
            var request = new RenderRequest(start, end, 320, 180, RenderPurpose.PreviewAudio, false, true, false, AudioLayout.PerTrack, 1, OutputTail.Preview, false);
            var pcm = await kit.RenderPcmAsync(project, catalog, request);
            var samples = new short[pcm.Length / 2];
            Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
            joined.AddRange(Left(samples, 2));
        }

        AssertAligned([.. joined], speed);
    }

    // 9 s of source, so 17 bursts are measured.
    private static long ClipFrames(int speed) => 9 * 30 * 100 / speed;

    private static float[] Left(short[] interleaved, int channels)
    {
        var left = new float[interleaved.Length / channels];
        for (var i = 0; i < left.Length; i++)
        {
            left[i] = interleaved[i * channels] / 32768f;
        }

        return left;
    }

    // The energy centroid of each burst against its ideal output position.
    private static void AssertAligned(float[] samples, int speed)
    {
        var errors = new List<double>();
        var sourceIn = SourceInMicros * 48_000 / 1_000_000;
        var spacingOut = TestMediaFixture.BurstSpacing * 100.0 / speed;
        var half = (int)(spacingOut / 2) - 10;
        for (var k = 0; ; k++)
        {
            var centre = TestMediaFixture.FirstBurst + ((long)k * TestMediaFixture.BurstSpacing) + ((TestMediaFixture.BurstLength - 1) / 2.0);
            if (centre - (TestMediaFixture.BurstLength / 2.0) < sourceIn)
            {
                continue;
            }

            var expected = (centre - sourceIn) * 100 / speed;
            if (expected + half >= samples.Length)
            {
                break;
            }

            double energy = 0, moment = 0;
            for (var i = (int)expected - half; i < (int)expected + half; i++)
            {
                var e = (double)samples[i] * samples[i];
                energy += e;
                moment += e * i;
            }

            Assert.True(energy > 0, $"Burst {k} is missing at sample {expected:F0}.");
            errors.Add((moment / energy) - expected);
        }

        Assert.True(errors.Count >= 15, $"Only {errors.Count} bursts were measured.");
        var mean = errors.Average();
        var detail = string.Join(' ', errors.Select(e => e.ToString("F0", CultureInfo.InvariantCulture)));
        Assert.True(Math.Abs(mean) <= MeanTolerance, $"Mean burst error {mean:F1} samples at {speed}%: {detail}");
        Assert.True(errors.All(e => Math.Abs(e) <= BurstTolerance), $"A burst is off by more than {BurstTolerance} samples at {speed}%: {detail}");
    }
}
