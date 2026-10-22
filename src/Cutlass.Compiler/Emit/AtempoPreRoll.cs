namespace Cutlass.Compiler.Emit;

/// <summary>
/// The atempo pre-roll (TECH_BRIEF 7.3): a sped-up or slowed-down audio segment is read from
/// <see cref="PreRollSamples"/> source samples before its first sample, run through atempo, and the output lead is cut
/// with <c>atrim=start_sample=H</c>. atempo (WSOLA) starts with a half-faded first window and puts its output early by a
/// lead that depends on the speed, so without the pre-roll every segment (and every playback window) started early,
/// faded in and skipped source audio at its seams.
/// </summary>
/// <remarks>
/// The leads were measured with ffmpeg N-126308 on 100 ms Hann-windowed tone bursts (997 Hz and 311 Hz, two seek
/// phases): the energy centroid of each burst against its ideal position <c>(source position) * 100 / speed</c>, after
/// the pre-roll. They are tabulated every 5 percent per atempo chain shape and interpolated linearly. A single click
/// cannot be placed this precisely: WSOLA moves an isolated sample by up to half a window depending on its phase, so the
/// table is calibrated on bursts, which is how music and speech behave. The integration test
/// <c>AtempoAlignmentTests</c> checks bursts land within 1 ms at 25, 50, 150, 200 and 300 percent.
/// </remarks>
internal static class AtempoPreRoll
{
    /// <summary>The pre-roll in 48 kHz source samples: 0.2 s, at least two 2048-sample atempo windows per stage.</summary>
    public const long PreRollSamples = 9600;

    /// <summary>The pre-roll in microseconds.</summary>
    public const long PreRollMicros = 200_000;

    // Speeds 25..50 (chain 0.5, f with f in [0.5, 1]), every 5 percent, in output samples.
    private static readonly int[] HalfThenFactor = [1523, 1277, 1091, 958, 860, 793];

    // Speeds 50..200 (one factor), every 5 percent. 100 percent never runs atempo; its value (atempo=1) only anchors
    // the interpolation for 96 to 104 percent.
    private static readonly int[] SingleFactor =
    [
        626, 566, 513, 470, 433, 412, 383, 360, 341, 322, 311, 300, 291, 282, 273, 266,
        258, 250, 245, 241, 240, 237, 233, 230, 228, 227, 229, 224, 221, 225, 218,
    ];

    // Speeds 200..400 (chain 2, f with f in [1, 2]), every 5 percent. 200 percent itself is one factor; its value here
    // (2, 1) only anchors the interpolation for 201 to 204 percent.
    private static readonly int[] DoubleThenFactor =
    [
        649, 628, 613, 601, 587, 578, 563, 554, 543, 529, 525, 511, 503, 493, 488, 481, 474, 468, 460, 455, 454,
        445, 442, 435, 433, 424, 421, 423, 417, 407, 411, 401, 404, 394, 391, 390, 388, 384, 380, 381, 409,
    ];

    /// <summary>
    /// How early atempo puts its output at a speed, in 48 kHz output samples (the calibrated lead, interpolated).
    /// </summary>
    /// <param name="speedPercent">The speed, clamped to 25..400.</param>
    /// <returns>The lead in output samples.</returns>
    public static long LeadSamples(int speedPercent)
    {
        var p = Math.Clamp(speedPercent, 25, 400);
        return p < 50 ? Interpolate(HalfThenFactor, 25, p)
            : p <= 200 ? Interpolate(SingleFactor, 50, p)
            : Interpolate(DoubleThenFactor, 200, p);
    }

    /// <summary>
    /// The output samples to cut after atempo: the pre-roll at the output rate, <c>round(9600 * 100 / speed)</c>,
    /// minus the lead.
    /// </summary>
    /// <param name="speedPercent">The speed in percent (not 100).</param>
    /// <returns>The <c>atrim=start_sample</c> value.</returns>
    public static long HeadTrimSamples(int speedPercent)
    {
        var p = Math.Clamp(speedPercent, 25, 400);
        var preRollOut = ((2 * PreRollSamples * 100) + p) / (2L * p);
        return Math.Max(0, preRollOut - LeadSamples(p));
    }

    private static long Interpolate(int[] table, int firstPercent, int p)
    {
        var offset = p - firstPercent;
        var i = Math.Min(offset / 5, table.Length - 1);
        var rest = offset - (i * 5);
        if (rest == 0 || i + 1 >= table.Length)
        {
            return table[i];
        }

        // Round half away from zero on the integer interpolation, so the result is exact and deterministic.
        var scaled = (table[i] * 5L) + ((table[i + 1] - table[i]) * (long)rest);
        return scaled >= 0 ? (scaled + 2) / 5 : -((-scaled + 2) / 5);
    }
}
