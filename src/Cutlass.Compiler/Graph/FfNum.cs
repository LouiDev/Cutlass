using System.Globalization;
using Cutlass.Core.Time;

namespace Cutlass.Compiler.Graph;

/// <summary>
/// The only number formatter for ffmpeg arguments and filtergraphs (TECH_BRIEF 7.7). Everything is formatted with the
/// invariant culture, so a de-DE machine still writes <c>1.5</c> and never <c>1,5</c>.
/// </summary>
public static class FfNum
{
    private const long MicrosPerSecond = 1_000_000;

    /// <summary>Formats microseconds as seconds with exactly 6 decimals, for example <c>12.345678</c>.</summary>
    /// <param name="micros">The time in microseconds (already floored).</param>
    /// <returns>The invariant text.</returns>
    public static string Seconds(long micros)
    {
        var negative = micros < 0;
        var magnitude = negative ? (ulong)(-(micros + 1)) + 1UL : (ulong)micros;
        var whole = magnitude / MicrosPerSecond;
        var fraction = magnitude % MicrosPerSecond;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(negative ? "-" : string.Empty)}{whole}.{fraction:D6}");
    }

    /// <summary>Formats exact seconds with 6 decimals, floored to the microsecond.</summary>
    /// <param name="seconds">The exact time in seconds.</param>
    /// <returns>The invariant text.</returns>
    public static string Seconds(Rational seconds) => Seconds(TimeMath.FloorMicros(seconds));

    /// <summary>Formats a decimal value with <c>F6</c> (6 decimals, rounded). Negative zero prints as <c>0.000000</c>.</summary>
    /// <param name="value">A finite value.</param>
    /// <returns>The invariant text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="value"/> is NaN or infinite.</exception>
    public static string Decimal(double value)
    {
        EnsureFinite(value);
        var text = value.ToString("F6", CultureInfo.InvariantCulture);
        return text == "-0.000000" ? "0.000000" : text;
    }

    /// <summary>
    /// Formats a colour alpha (normally 0 to 1) with up to 3 decimals and trailing zeros trimmed, for example
    /// <c>0.5</c>, <c>1</c> or <c>0.333</c>.
    /// </summary>
    /// <param name="value">A finite value.</param>
    /// <returns>The invariant text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="value"/> is NaN or infinite.</exception>
    public static string Alpha(double value)
    {
        EnsureFinite(value);

        // The brief does not say how to reduce to 3 decimals; round half away from zero, the simplest reading.
        var rounded = Math.Round(value, 3, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.###", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }

    /// <summary>Formats a frame rate as <c>30</c> or <c>30000/1001</c>.</summary>
    /// <param name="rate">The rate.</param>
    /// <returns>The invariant text.</returns>
    public static string Rate(FrameRate rate) => rate.Value.ToFfString();

    /// <summary>Formats a rational as <c>30</c> or <c>30000/1001</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The invariant text.</returns>
    public static string Rate(Rational value) => value.ToFfString();

    /// <summary>Formats an integer (frames, samples, pixels).</summary>
    /// <param name="value">The value.</param>
    /// <returns>The invariant text.</returns>
    public static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static void EnsureFinite(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Only finite numbers can be passed to ffmpeg.");
        }
    }
}
