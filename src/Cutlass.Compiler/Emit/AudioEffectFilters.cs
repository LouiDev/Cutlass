using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;

namespace Cutlass.Compiler.Emit;

/// <summary>
/// The per-clip audio effect filters (TECH_BRIEF 7.3 step 3a, effects batch 3). Every method returns null when its
/// value is neutral, so a clip without effects emits exactly the chain it emitted before. They sit between
/// <c>aformat</c> (48 kHz stereo fltp) and <c>apad</c>; each is 1:1 in sample count, so the segment still seals to
/// its exact length. Dynamics thresholds are linear (no dB suffix) and every number goes through <see cref="FfNum"/>.
/// </summary>
internal static class AudioEffectFilters
{
    /// <summary>Appends every active effect in the documented order: high-pass, low-pass, bass, treble, gate, compressor, then mono or pan.</summary>
    /// <param name="chain">The segment chain, positioned after <c>aformat</c>.</param>
    /// <param name="effects">The clip's effects.</param>
    public static void Append(GraphChain chain, AudioEffects effects)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(effects);
        Then(chain, HighPass(effects));
        Then(chain, LowPass(effects));
        Then(chain, Bass(effects));
        Then(chain, Treble(effects));
        Then(chain, Gate(effects));
        Then(chain, Compressor(effects));
        Then(chain, Stereo(effects));
    }

    /// <summary>The <c>highpass</c> filter.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the high-pass is off.</returns>
    public static string? HighPass(AudioEffects effects) =>
        effects.HighPassHz > 0 ? "highpass=f=" + FfNum.Decimal(effects.HighPassHz) : null;

    /// <summary>The <c>lowpass</c> filter.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the low-pass is off.</returns>
    public static string? LowPass(AudioEffects effects) =>
        effects.LowPassHz > 0 ? "lowpass=f=" + FfNum.Decimal(effects.LowPassHz) : null;

    /// <summary>The <c>bass</c> shelf at 100 Hz.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null at 0 dB.</returns>
    public static string? Bass(AudioEffects effects) =>
        effects.BassDb != 0 ? "bass=g=" + FfNum.Decimal(effects.BassDb) + ":f=100" : null;

    /// <summary>The <c>treble</c> shelf at 3 kHz.</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null at 0 dB.</returns>
    public static string? Treble(AudioEffects effects) =>
        effects.TrebleDb != 0 ? "treble=g=" + FfNum.Decimal(effects.TrebleDb) + ":f=3000" : null;

    /// <summary>
    /// The <c>agate</c> filter: a 4:1 downward expander with a fixed 5 ms attack and 100 ms release. The range is set
    /// explicitly because the default (0.06125) closes the gate by only 24 dB.
    /// </summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the gate is off.</returns>
    public static string? Gate(AudioEffects effects) =>
        effects.Gate
            ? "agate=threshold=" + FfNum.Decimal(Linear(effects.GateThresholdDb)) + ":ratio=4:attack=5:release=100:range=0.001000"
            : null;

    /// <summary>The <c>acompressor</c> filter with a hard knee, 5 ms attack, 50 ms release and no make-up gain (clip gain follows it).</summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the compressor is off.</returns>
    public static string? Compressor(AudioEffects effects) =>
        effects.Compressor
            ? "acompressor=threshold=" + FfNum.Decimal(Math.Max(0.001, Linear(effects.CompressorThresholdDb)))
                + ":ratio=" + FfNum.Decimal(effects.CompressorRatio) + ":attack=5:release=50:knee=1:makeup=1"
            : null;

    /// <summary>
    /// The stereo field, last: a mono sum that stays stereo, or a constant-power pan (theta = (pan + 1) * PI / 4,
    /// left = cos, right = sin). The spec uses <c>|</c> separators and no <c>:</c> or <c>,</c>, so it needs no quoting.
    /// </summary>
    /// <param name="effects">The effects.</param>
    /// <returns>The filter text, or null when the clip is neither mono nor panned.</returns>
    public static string? Stereo(AudioEffects effects)
    {
        if (effects.Mono)
        {
            return "pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0+0.5*c1";
        }

        if (effects.Pan == 0)
        {
            return null;
        }

        var theta = (effects.Pan + 1) * Math.PI / 4;
        return "pan=stereo|c0=" + FfNum.Decimal(Math.Cos(theta)) + "*c0|c1=" + FfNum.Decimal(Math.Sin(theta)) + "*c1";
    }

    /// <summary>A dBFS level as a linear amplitude.</summary>
    /// <param name="db">The level in dB.</param>
    /// <returns>10^(db/20).</returns>
    public static double Linear(double db) => Math.Pow(10, db / 20);

    private static void Then(GraphChain chain, string? filter)
    {
        if (filter is not null)
        {
            chain.Then(filter);
        }
    }
}
