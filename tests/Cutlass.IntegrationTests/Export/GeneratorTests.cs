using System.Globalization;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// Generator clips of effects batch 5 on real ffmpeg (TECH_BRIEF 7.3 and 11.2): a colour matte renders its colour, the
/// bars differ across the frame, and a tone has the documented per-channel level, the exact sample count and the same
/// samples in a later window or across a split as in the full render.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class GeneratorTests
{
    private const int W = 320;
    private const int H = 180;

    // The tone level: aevalsrc writes the sine with amplitude 1/8 on each stereo channel, so each channel peaks at
    // 20 log10(1/8) = -18.06 dBFS and has RMS (1/8)/sqrt 2, that is 20 log10(1/8) - 3.01 = -21.07 dBFS.
    private static readonly double TonePeakDb = 20 * Math.Log10(1.0 / 8);
    private static readonly double ToneDb = 20 * Math.Log10(1.0 / 8 / Math.Sqrt(2));

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public GeneratorTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task AColourMatteRendersItsColour()
    {
        await using var kit = await ExportKit.CreateAsync();
        var matte = GeneratorClip.Create("c_m", 0, 10, GeneratorKind.ColorMatte) with { Color = "#FF0000" };
        var project = Project(W, H, FrameRate.Fps30, [], Track("tr_v1", TrackKind.Video, [matte]));

        var frames = await kit.RenderBgraAsync(project, new TestRenderCatalog(), Preview(0, 10, W, H));

        var (b, g, r, a) = Pixel(frames, 5, W / 2, H / 2);
        Assert.True(r >= 245 && g <= 12 && b <= 12 && a == 255, string.Create(CultureInfo.InvariantCulture, $"The matte centre is BGRA {b},{g},{r},{a}, not red."));

        // A transformed matte at half size leaves the canvas corner black.
        var small = matte with { Video = VideoProps.Default with { Transform = Transform.Identity with { Scale = 0.5 } } };
        var placed = await kit.RenderBgraAsync(Project(W, H, FrameRate.Fps30, [], Track("tr_v1", TrackKind.Video, [small])), new TestRenderCatalog(), Preview(0, 2, W, H, RenderPurpose.PreviewFrame));
        Assert.True(Pixel(placed, 0, W / 2, H / 2).R >= 245, "The scaled matte centre is not red.");
        Assert.True(Pixel(placed, 0, 10, 10).R <= 20, "The corner outside the scaled matte is not black.");
    }

    [FfmpegFact]
    public async Task BarsDifferBetweenTheLeftAndTheRightThird()
    {
        await using var kit = await ExportKit.CreateAsync();
        var bars = GeneratorClip.Create("c_b", 0, 5, GeneratorKind.Bars);
        var project = Project(W, H, FrameRate.Fps30, [], Track("tr_v1", TrackKind.Video, [bars]));

        var frames = await kit.RenderBgraAsync(project, new TestRenderCatalog(), Preview(0, 5, W, H));

        var left = Mean(frames, 2, 0, W / 3, H / 4);
        var right = Mean(frames, 2, 2 * W / 3, W, H / 4);
        var distance = Math.Abs(left.B - right.B) + Math.Abs(left.G - right.G) + Math.Abs(left.R - right.R);
        Assert.True(distance > 60, string.Create(CultureInfo.InvariantCulture, $"The left third {left} and the right third {right} of the bars are too alike."));
    }

    [FfmpegFact]
    public async Task AToneExportsAtMinus18DbfsPeakPerChannelWithTheExactSampleCount()
    {
        const long Frames = 60;
        await using var kit = await ExportKit.CreateAsync();
        var tone = GeneratorClip.Create("c_t", 0, Frames, GeneratorKind.Tone);
        var quieter = tone with { Id = "c_q", Audio = AudioProps.Default with { GainDb = -6 } };

        var samples = await ExportToneAsync(kit, tone, "tone", Frames);
        Assert.Equal(TimeMath.SamplesAt(Frames, FrameRate.Fps30), samples.Length / 2);
        Assert.InRange(Rms(samples, 0), ToneDb - 0.5, ToneDb + 0.5);
        Assert.InRange(Rms(samples, 1), ToneDb - 0.5, ToneDb + 0.5);
        Assert.InRange(Peak(samples, 0), TonePeakDb - 0.1, TonePeakDb + 0.1);
        Assert.InRange(Peak(samples, 1), TonePeakDb - 0.1, TonePeakDb + 0.1);

        var gained = await ExportToneAsync(kit, quieter, "tone_gain", Frames);
        Assert.InRange(Rms(gained, 0), ToneDb - 6.5, ToneDb - 5.5);

        // The Tone fixture (the same 1 kHz sine at amplitude 1/8 as a mono file) through the media path is upmixed to
        // stereo at -3.01 dB per channel (1/sqrt 2), so it lands 3.01 dB below the generator.
        var asset = await kit.AssetAsync("m_tone", _media.Tone);
        var catalog = await kit.CatalogAsync([asset]);
        var project = Project(W, H, FrameRate.Fps30, [asset], Track("tr_a1", TrackKind.Audio, [A("c_a", 0, Frames, asset.Id, 0)]));
        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Wav, "tone_fixture", 0, Frames));
        ExportKit.AssertCompleted(job);
        var fixture = await ExportKit.DecodeAudioAsync(job.OutputPath);
        Assert.InRange(Rms(fixture, 0) - Rms(samples, 0), -3.11, -2.91);
    }

    [FfmpegFact]
    public async Task ALaterToneWindowContinuesThePhaseOfTheFullRender()
    {
        // The tone phase counts from timeline sample 0 (TECH_BRIEF 7.3, generators), so a window that starts inside the
        // tone continues the full render; chunked playback then joins without a click.
        await using var kit = await ExportKit.CreateAsync();
        var tone = GeneratorClip.Create("c_t", 5, 55, GeneratorKind.Tone) with { ToneHz = 441 };
        var project = Project(W, H, FrameRate.Fps30, [], Track("tr_a1", TrackKind.Audio, [tone]));

        var full = await kit.RenderPcmAsync(project, new TestRenderCatalog(), AudioRequest(0, 60));
        var later = await kit.RenderPcmAsync(project, new TestRenderCatalog(), AudioRequest(23, 60));

        var offset = (int)(TimeMath.SamplesAt(23, FrameRate.Fps30) * 4);
        Assert.Equal(full.Length - offset, later.Length);
        var maxDifference = 0;
        for (var i = 0; i < later.Length; i += 2)
        {
            var a = BitConverter.ToInt16(full, offset + i);
            var b = BitConverter.ToInt16(later, i);
            maxDifference = Math.Max(maxDifference, Math.Abs(a - b));
        }

        Assert.True(maxDifference <= 1, $"The later window differs from the full render by up to {maxDifference} LSB.");
    }

    [FfmpegFact]
    public async Task ASplitToneRendersTheSameSamplesAsTheUnsplitTone()
    {
        // 441 Hz at 30 fps: a frame holds 1600 samples, which is not a whole number of periods (48000 / 441 = 108.84),
        // so a phase that restarted at the cut would click there.
        await using var kit = await ExportKit.CreateAsync();
        var tone = GeneratorClip.Create("c_t", 5, 55, GeneratorKind.Tone) with { ToneHz = 441 };
        var head = tone with { Duration = 18 };
        var tail = tone with { Id = "c_t2", Start = 23, Duration = 37 };

        var whole = await kit.RenderPcmAsync(Project(W, H, FrameRate.Fps30, [], Track("tr_a1", TrackKind.Audio, [tone])), new TestRenderCatalog(), AudioRequest(0, 60));
        var split = await kit.RenderPcmAsync(Project(W, H, FrameRate.Fps30, [], Track("tr_a1", TrackKind.Audio, [head, tail])), new TestRenderCatalog(), AudioRequest(0, 60));

        Assert.Equal(whole.Length, split.Length);
        var maxDifference = 0;
        for (var i = 0; i < whole.Length; i += 2)
        {
            maxDifference = Math.Max(maxDifference, Math.Abs(BitConverter.ToInt16(whole, i) - BitConverter.ToInt16(split, i)));
        }

        Assert.True(maxDifference <= 1, $"The split tone differs from the unsplit tone by up to {maxDifference} LSB.");
    }

    private static RenderRequest AudioRequest(long start, long end) =>
        new(start, end, W, H, RenderPurpose.PreviewAudio, false, true, false, AudioLayout.Mixed, 1, OutputTail.Preview, false);

    private static async Task<short[]> ExportToneAsync(ExportKit kit, GeneratorClip tone, string name, long frames)
    {
        var project = Project(W, H, FrameRate.Fps30, [], Track("tr_a1", TrackKind.Audio, [tone]));
        var job = await kit.ExportAsync(kit.Request(project, new TestRenderCatalog(), ExportPresets.Wav, name, 0, frames));
        ExportKit.AssertCompleted(job);
        Assert.Empty(job.Warnings);
        return await ExportKit.DecodeAudioAsync(job.OutputPath);
    }

    private static (int B, int G, int R, int A) Pixel(byte[] bgra, int frame, int x, int y)
    {
        var i = (frame * W * H * 4) + (((y * W) + x) * 4);
        return (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]);
    }

    private static (double B, double G, double R) Mean(byte[] bgra, int frame, int fromX, int toX, int y)
    {
        double b = 0, g = 0, r = 0;
        for (var x = fromX; x < toX; x++)
        {
            var p = Pixel(bgra, frame, x, y);
            b += p.B;
            g += p.G;
            r += p.R;
        }

        var n = toX - fromX;
        return (b / n, g / n, r / n);
    }

    // Per-channel peak in dBFS.
    private static double Peak(short[] interleaved, int channel)
    {
        var peak = 0;
        for (var i = channel; i < interleaved.Length; i += 2)
        {
            peak = Math.Max(peak, Math.Abs((int)interleaved[i]));
        }

        return 20 * Math.Log10(peak / 32768.0);
    }

    // Per-channel RMS in dBFS without the first and last 0.1 s.
    private static double Rms(short[] interleaved, int channel)
    {
        const int Skip = 4_800;
        var frames = interleaved.Length / 2;
        double sum = 0;
        var count = 0;
        for (var i = Skip; i < frames - Skip; i++)
        {
            var v = interleaved[(i * 2) + channel] / 32768.0;
            sum += v * v;
            count++;
        }

        return 20 * Math.Log10(Math.Sqrt(sum / count));
    }
}
