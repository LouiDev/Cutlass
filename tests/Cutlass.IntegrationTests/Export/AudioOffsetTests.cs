using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// A/V offsets survive export (TECH_BRIEF 7.3 and 11.2): audio that starts after its file's start time keeps its
/// delay (<c>aresample first_pts=0</c>, never <c>PTS-STARTPTS</c>), and a flash/impulse sync clip stays within 1 ms.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class AudioOffsetTests
{
    private const short ImpulseThreshold = 16_000;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public AudioOffsetTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegTheory]
    [InlineData(0, 24_000)]
    [InlineData(200_000, 14_400)]
    public async Task DelayedAudioKeepsItsOffset(long sourceInMicros, long expectedSample)
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_d", _media.DelayedAudio);
        var catalog = await kit.CatalogAsync([asset]);
        var project = Project(
            320,
            180,
            FrameRate.Fps30,
            [asset],
            Track("tr_v1", TrackKind.Video, [V("c_v", 0, 60, "m_d", Micros(sourceInMicros))]),
            Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 60, "m_d", Micros(sourceInMicros))]));

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Wav, "delayed", 0, 60));

        ExportKit.AssertCompleted(job);
        var samples = await ExportKit.DecodeAudioAsync(job.OutputPath);
        Assert.Equal(96_000, samples.Length / 2);
        Assert.Equal(expectedSample, ExportKit.FirstPeak(samples, ImpulseThreshold));
        Assert.Equal(expectedSample + 48_000, ExportKit.FirstPeak(samples, ImpulseThreshold, expectedSample + 1));
    }

    [FfmpegFact]
    public async Task DelayedAudioKeepsItsOffsetInAPreviewWindow()
    {
        // The same path serves playback: PCM for the window [15, 45) with the impulse at 0.5 s lands at 24000 - 24000.
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_d", _media.DelayedAudio);
        var catalog = await kit.CatalogAsync([asset]);
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 60, "m_d", 0)]));
        var request = new Cutlass.Compiler.RenderRequest(
            15, 45, 320, 180, Cutlass.Compiler.RenderPurpose.PreviewAudio, false, true, false, Cutlass.Compiler.AudioLayout.PerTrack, 1, OutputTail.Preview, false);

        var pcm = await kit.RenderPcmAsync(project, catalog, request);

        var samples = new short[pcm.Length / 2];
        Buffer.BlockCopy(pcm, 0, samples, 0, pcm.Length);
        Assert.Equal(48_000, samples.Length / 2);
        Assert.Equal(0, ExportKit.FirstPeak(samples, ImpulseThreshold));
    }

    [FfmpegFact]
    public async Task SyncClipStaysWithinOneMillisecond()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_s", _media.Sync);
        var catalog = await kit.CatalogAsync([asset]);
        var sourceIn = 15 * (TimeMath.FlicksPerSecond / 30);
        var project = Project(
            320,
            180,
            FrameRate.Fps30,
            [asset],
            Track("tr_v1", TrackKind.Video, [V("c_v", 0, 150, "m_s", sourceIn)]),
            Track("tr_a1", TrackKind.Audio, [A("c_a", 0, 150, "m_s", sourceIn)]));

        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.ProRes, "sync", 0, 150));

        ExportKit.AssertCompleted(job);
        var gray = await ExportKit.DecodeGrayAsync(job.OutputPath);
        var flashes = new List<long>();
        for (long f = 0; f < 150; f++)
        {
            if (gray[(int)((f * 320 * 180) + (90 * 320) + 160)] > 128)
            {
                flashes.Add(f);
            }
        }

        var peaks = ExportKit.Peaks(await ExportKit.DecodeAudioAsync(job.OutputPath), ImpulseThreshold);
        Assert.Equal([15L, 45, 75, 105, 135], flashes);
        Assert.Equal(flashes.Count, peaks.Length);
        for (var i = 0; i < flashes.Count; i++)
        {
            var flashSample = TimeMath.SamplesAt(flashes[i], FrameRate.Fps30);
            Assert.InRange(peaks[i], flashSample - 48, flashSample + 48);
        }
    }
}
