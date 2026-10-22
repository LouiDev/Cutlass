using System.Collections.Immutable;
using System.Globalization;

namespace Cutlass.Core.Time;

/// <summary>
/// A sequence or media frame rate. The eight <see cref="Supported"/> rates divide <see cref="TimeMath.FlicksPerSecond"/>
/// exactly, so <see cref="FlicksPerFrame"/> is exact for them.
/// </summary>
/// <param name="Value">The rate in frames per second, for example 30000/1001.</param>
public readonly record struct FrameRate(Rational Value)
{
    /// <summary>23.976 fps (24000/1001).</summary>
    public static readonly FrameRate Fps23976 = new(new Rational(24000, 1001));

    /// <summary>24 fps.</summary>
    public static readonly FrameRate Fps24 = new(new Rational(24, 1));

    /// <summary>25 fps.</summary>
    public static readonly FrameRate Fps25 = new(new Rational(25, 1));

    /// <summary>29.97 fps (30000/1001).</summary>
    public static readonly FrameRate Fps2997 = new(new Rational(30000, 1001));

    /// <summary>30 fps.</summary>
    public static readonly FrameRate Fps30 = new(new Rational(30, 1));

    /// <summary>50 fps.</summary>
    public static readonly FrameRate Fps50 = new(new Rational(50, 1));

    /// <summary>59.94 fps (60000/1001).</summary>
    public static readonly FrameRate Fps5994 = new(new Rational(60000, 1001));

    /// <summary>60 fps.</summary>
    public static readonly FrameRate Fps60 = new(new Rational(60, 1));

    /// <summary>Every rate a sequence may use, in ascending order.</summary>
    public static readonly ImmutableArray<FrameRate> Supported =
        [Fps23976, Fps24, Fps25, Fps2997, Fps30, Fps50, Fps5994, Fps60];

    /// <summary>
    /// Flicks per frame: <c>FlicksPerSecond * Den / Num</c>, floored. Exact for every <see cref="Supported"/> rate
    /// (for example 23,543,520 at 30000/1001).
    /// </summary>
    public long FlicksPerFrame => (long)((Int128)TimeMath.FlicksPerSecond * Value.Den / Value.Num);

    /// <summary>
    /// The integer frame count per timecode second (non-drop): the rate rounded to the nearest integer,
    /// so 30000/1001 counts 30 frames and 24000/1001 counts 24.
    /// </summary>
    public int TimecodeBase => (int)Math.Max(1L, ((2 * Value.Num) + Value.Den) / (2 * Value.Den));

    /// <summary>True when the rate is one of the <see cref="Supported"/> sequence rates.</summary>
    public bool IsSupported => Supported.Contains(this);

    /// <summary>Human readable rate with up to three decimals, for example <c>"29.97"</c> or <c>"23.976"</c>.</summary>
    public string DisplayName => Value.ToDouble().ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Returns the supported rate closest to a measured rate, using exact rational distance. Ties pick the lower rate.
    /// </summary>
    /// <param name="measured">The measured rate, for example ffprobe's <c>r_frame_rate</c>.</param>
    /// <returns>The nearest supported rate, or 30 fps when <paramref name="measured"/> is not positive.</returns>
    public static FrameRate Nearest(Rational measured)
    {
        // Unspecified in the brief: a missing or non-positive rate falls back to 30 fps, the simplest neutral default.
        if (!measured.IsPositive)
        {
            return Fps30;
        }

        var best = Supported[0];
        Int128 bestDistNum = 0;
        long bestDen = 1;
        var first = true;
        foreach (var rate in Supported)
        {
            // |measured - rate| = |m.Num*r.Den - r.Num*m.Den| / (m.Den*r.Den). m.Den is common to all candidates,
            // so compare |diff| / r.Den across candidates by cross multiplication.
            var diff = ((Int128)measured.Num * rate.Value.Den) - ((Int128)rate.Value.Num * measured.Den);
            if (diff < 0)
            {
                diff = -diff;
            }

            if (first || diff * bestDen < bestDistNum * rate.Value.Den)
            {
                best = rate;
                bestDistNum = diff;
                bestDen = rate.Value.Den;
                first = false;
            }
        }

        return best;
    }

    /// <inheritdoc />
    public override string ToString() => Value.ToFfString();
}
