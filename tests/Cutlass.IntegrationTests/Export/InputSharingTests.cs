using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// A clip used twice, far apart on the timeline, reads its source through two inputs (TECH_BRIEF 7.7), and the render
/// still shows the right frames and plays the right audio in both places.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class InputSharingTests
{
    private const short ImpulseThreshold = 16_000;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public InputSharingTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task DuplicatedOracleClipShowsTheSameFramesInBothPlaces()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_o", _media.Oracle25Intra);
        var catalog = await kit.CatalogAsync([asset]);
        var rate = FrameRate.Fps25;
        var project = Project(
            320,
            180,
            rate,
            [asset],
            Track("tr_v1", TrackKind.Video, [V("c_1", 0, 25, "m_o", Micros(2_000_000)), V("c_2", 250, 25, "m_o", Micros(2_000_000))]));
        var request = Preview(0, 275, 320, 180);

        Assert.Equal(2, TimelineCompiler.Compile(project, catalog, request).Inputs.Count);
        var frames = await kit.RenderBgraAsync(project, catalog, request);

        var failures = new StringBuilder();
        for (long f = 0; f < 275; f++)
        {
            var actual = OracleReader.DecodeBgra(frames.AsSpan((int)(f * 320 * 180 * 4), 320 * 180 * 4), 320, 180);
            var expected = f < 25 ? TestMediaFixture.ExpectedOracleValue(50 + f)
                : f >= 250 ? TestMediaFixture.ExpectedOracleValue(50 + f - 250)
                : 0;
            if (actual != expected)
            {
                failures.Append(System.Globalization.CultureInfo.InvariantCulture, $"frame {f}: expected {expected}, got {actual}; ");
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
    }

    [FfmpegFact]
    public async Task DuplicatedLinkedClipExportsFlashesAndImpulsesInBothPlaces()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_s", _media.Sync);
        var catalog = await kit.CatalogAsync([asset]);
        var project = Project(
            320,
            180,
            FrameRate.Fps30,
            [asset],
            Track("tr_v1", TrackKind.Video, [V("c_v1", 0, 60, "m_s", 0), V("c_v2", 240, 60, "m_s", 0)]),
            Track("tr_a1", TrackKind.Audio, [A("c_a1", 0, 60, "m_s", 0), A("c_a2", 240, 60, "m_s", 0)]));
        var request = kit.Request(project, catalog, ExportPresets.ProRes, "duplicated", 0, 300);

        // Linked audio and video of each copy share one input; the two copies do not.
        var render = TimelineCompiler.Compile(
            project,
            catalog,
            new RenderRequest(0, 300, 320, 180, RenderPurpose.Export, true, true, false, AudioLayout.Mixed, 1, OutputTail.ForExport("yuv422p10le"), false));
        Assert.Equal(2, render.Inputs.Count);

        var job = await kit.ExportAsync(request);

        ExportKit.AssertCompleted(job);
        var gray = await ExportKit.DecodeGrayAsync(job.OutputPath);
        var flashes = new List<long>();
        for (long f = 0; f < 300; f++)
        {
            if (gray[(int)((f * 320 * 180) + (90 * 320) + 160)] > 128)
            {
                flashes.Add(f);
            }
        }

        Assert.Equal([0L, 30, 240, 270], flashes);
        var peaks = ExportKit.Peaks(await ExportKit.DecodeAudioAsync(job.OutputPath), ImpulseThreshold);
        Assert.Equal(4, peaks.Length);
        long[] expected = [0, 48_000, 384_000, 432_000];
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(peaks[i], expected[i], expected[i] + 48);
        }
    }
}
