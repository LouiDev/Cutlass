using System.Globalization;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Infrastructure;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Export;

/// <summary>
/// The per-clip audio effects of effects batch 3 on real ffmpeg (TECH_BRIEF 7.3 step 3a and 11.2). Each case exports
/// the 1 kHz sine oracle twice as WAV, once plain and once with the effect, and compares the per-channel RMS over the
/// middle of the clip (0.5 s skipped at each end), so the mono-to-stereo upmix and the filter settling cancel. The
/// mono downmix uses a stereo file whose channels differ. Every export keeps the exact sample count. Two oracles are
/// bounds rather than figures: the treble shelf's response at 1 kHz depends on the shelf convention (an RBJ high shelf
/// at 3 kHz with -12 dB takes 0.19 dB at Q 0.707 and 1.24 dB at Q 0.5), and ffmpeg's "rms" compressor detector with a
/// 5 ms attack rides between the RMS and the peak of a steady sine, so its gain reduction lies between the two laws.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class AudioEffectsTests
{
    private const long Frames = 150;

    /// <summary>0.5 s at 48 kHz skipped at both ends of the RMS window.</summary>
    private const int Skip = 24_000;

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public AudioEffectsTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegTheory]
    [InlineData("lowPass200")]
    [InlineData("highPass200")]
    [InlineData("highPass2000")]
    [InlineData("bass-12")]
    [InlineData("treble-12")]
    [InlineData("compressor-30")]
    [InlineData("compressor-10")]
    [InlineData("gate-10")]
    [InlineData("gate-40")]
    public async Task EachEffectMovesTheSineOracleAsTheFilterMathSays(string effect)
    {
        var (effects, minDelta, maxDelta) = effect switch
        {
            "lowPass200" => (AudioEffects.None with { LowPassHz = 200 }, double.NegativeInfinity, -20.0),
            "highPass200" => (AudioEffects.None with { HighPassHz = 200 }, -0.5, 0.5),
            "highPass2000" => (AudioEffects.None with { HighPassHz = 2000 }, double.NegativeInfinity, -10.0),
            "bass-12" => (AudioEffects.None with { BassDb = -12 }, -0.5, 0.5),
            // 1 kHz is on the skirt of the 3 kHz shelf: a clear drop, but far from the 12 dB the shelf reaches (see the class summary).
            "treble-12" => (AudioEffects.None with { TrebleDb = -12 }, -2.0, -0.1),
            // The per-channel tone is -24.08 dBFS RMS and -21.07 dBFS peak, so a -30 dB threshold at 4:1 takes between
            // (30 - 24.08) * 0.75 = 4.4 dB (RMS law) and (30 - 21.07) * 0.75 = 6.7 dB (peak law).
            "compressor-30" => (AudioEffects.None with { Compressor = true, CompressorThresholdDb = -30, CompressorRatio = 4 }, -7.0, -4.2),
            "compressor-10" => (AudioEffects.None with { Compressor = true, CompressorThresholdDb = -10, CompressorRatio = 4 }, -0.2, 0.2),
            "gate-10" => (AudioEffects.None with { Gate = true, GateThresholdDb = -10 }, double.NegativeInfinity, double.NaN),
            _ => (AudioEffects.None with { Gate = true, GateThresholdDb = -40 }, -0.2, 0.2),
        };

        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_tone", _media.Tone);
        var catalog = await kit.CatalogAsync([asset]);
        var reference = await LevelsAsync(kit, asset, catalog, AudioProps.Default, "ref_" + effect, Frames);
        var filtered = await LevelsAsync(kit, asset, catalog, AudioProps.Default with { Effects = effects }, "fx_" + effect, Frames);

        for (var ch = 0; ch < 2; ch++)
        {
            var delta = filtered[ch] - reference[ch];
            if (double.IsNaN(maxDelta))
            {
                // A closed gate: the absolute level is what matters.
                Assert.True(filtered[ch] < -55, Describe(effect, ch, reference[ch], filtered[ch], "below -55 dBFS"));
                continue;
            }

            Assert.True(delta >= minDelta && delta <= maxDelta, Describe(effect, ch, reference[ch], filtered[ch], $"delta in [{minDelta:F2}, {maxDelta:F2}] dB"));
        }
    }

    [FfmpegFact]
    public async Task PanHardRightSilencesTheLeftAndKeepsTheRightAtThePanLaw()
    {
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_tone", _media.Tone);
        var catalog = await kit.CatalogAsync([asset]);
        var reference = await LevelsAsync(kit, asset, catalog, AudioProps.Default, "ref_pan", Frames);
        var panned = await LevelsAsync(kit, asset, catalog, AudioProps.Default with { Effects = AudioEffects.None with { Pan = 1 } }, "fx_pan", Frames);

        var theta = (1 + 1) * Math.PI / 4;
        var expectedRight = reference[1] + (20 * Math.Log10(Math.Sin(theta)));
        Assert.True(panned[0] <= reference[0] - 40, Describe("pan", 0, reference[0], panned[0], "at least 40 dB below the reference"));
        Assert.True(Math.Abs(panned[1] - expectedRight) <= 1, Describe("pan", 1, expectedRight, panned[1], "within 1 dB of the pan law"));
    }

    [FfmpegFact]
    public async Task MonoSumsBothChannelsAndStaysStereo()
    {
        // The stereo fixture is 4 s long, so the clip is 100 frames here (3.33 s) instead of 150.
        const long MonoFrames = 100;
        await using var kit = await ExportKit.CreateAsync();
        var asset = await kit.AssetAsync("m_st", _media.StereoTones);
        var catalog = await kit.CatalogAsync([asset]);

        var before = await DecodeAsync(kit, asset, catalog, AudioProps.Default, "ref_mono", MonoFrames);
        var after = await DecodeAsync(kit, asset, catalog, AudioProps.Default with { Effects = AudioEffects.None with { Mono = true } }, "fx_mono", MonoFrames);

        Assert.True(MaxChannelDifference(before) > 1000, "The stereo fixture's channels should differ before the downmix.");
        Assert.True(MaxChannelDifference(after) <= 1, $"After the mono downmix the channels differ by up to {MaxChannelDifference(after)} LSB.");
        Assert.True(Rms(after, 0) > -20, $"The mono sum is too quiet: {Rms(after, 0):F2} dBFS.");
    }

    private static string Describe(string effect, int channel, double reference, double actual, string expectation) =>
        string.Create(CultureInfo.InvariantCulture, $"{effect} channel {channel}: reference {reference:F2} dBFS, got {actual:F2} dBFS, expected {expectation}.");

    private static async Task<double[]> LevelsAsync(ExportKit kit, MediaAsset asset, TestRenderCatalog catalog, AudioProps props, string name, long frames)
    {
        var samples = await DecodeAsync(kit, asset, catalog, props, name, frames);
        return [Rms(samples, 0), Rms(samples, 1)];
    }

    private static async Task<short[]> DecodeAsync(ExportKit kit, MediaAsset asset, TestRenderCatalog catalog, AudioProps props, string name, long frames)
    {
        var project = Project(320, 180, FrameRate.Fps30, [asset], Track("tr_a1", TrackKind.Audio, [A("c_a", 0, frames, asset.Id, 0, audio: props)]));
        var job = await kit.ExportAsync(kit.Request(project, catalog, ExportPresets.Wav, name, 0, frames));
        ExportKit.AssertCompleted(job);
        var samples = await ExportKit.DecodeAudioAsync(job.OutputPath);
        Assert.Equal(TimeMath.SamplesAt(frames, FrameRate.Fps30), samples.Length / 2);
        return samples;
    }

    // Per-channel RMS in dBFS over the middle of the clip.
    private static double Rms(short[] interleaved, int channel)
    {
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

    private static int MaxChannelDifference(short[] interleaved)
    {
        var max = 0;
        var frames = interleaved.Length / 2;
        for (var i = Skip; i < frames - Skip; i++)
        {
            max = Math.Max(max, Math.Abs(interleaved[i * 2] - interleaved[(i * 2) + 1]));
        }

        return max;
    }
}
