using Cutlass.Core.Model;

namespace Cutlass.Core.Time;

/// <summary>
/// Exact conversions between timeline frames, audio samples, flicks and microseconds. All divisions floor
/// (towards negative infinity) and use 128-bit intermediates, so there is no drift at any length.
/// </summary>
public static class TimeMath
{
    /// <summary>Flicks per second. Every supported frame rate divides it exactly.</summary>
    public const long FlicksPerSecond = 705_600_000;

    /// <summary>The fixed audio sample rate of every sequence and every audio graph.</summary>
    public const int SampleRate = 48_000;

    /// <summary>Microseconds per second.</summary>
    public const long MicrosPerSecond = 1_000_000;

    /// <summary>
    /// The first audio sample of timeline frame <paramref name="frame"/>: <c>floor(frame * 48000 * den / num)</c>.
    /// Every sample position in the app must come from this function.
    /// </summary>
    /// <param name="frame">The absolute timeline frame.</param>
    /// <param name="r">The sequence rate.</param>
    /// <returns>The sample index.</returns>
    public static long SamplesAt(long frame, FrameRate r) =>
        (long)FloorDiv((Int128)frame * SampleRate * r.Value.Den, r.Value.Num);

    /// <summary>The largest frame <c>f</c> with <c>SamplesAt(f) &lt;= sample</c>.</summary>
    /// <param name="sample">The sample index.</param>
    /// <param name="r">The sequence rate.</param>
    /// <returns>The frame containing that sample.</returns>
    public static long FrameAtSample(long sample, FrameRate r)
    {
        // floor(f*S*den/num) <= sample holds exactly when f*S*den < (sample+1)*num,
        // that is when f <= ((sample+1)*num - 1) / (S*den), floored.
        var numerator = (((Int128)sample + 1) * r.Value.Num) - 1;
        return (long)FloorDiv(numerator, (Int128)SampleRate * r.Value.Den);
    }

    /// <summary>
    /// The source position (flicks from <c>format.start_time</c>) shown by <paramref name="c"/> at timeline frame
    /// <paramref name="frame"/>: <c>SourceInFlicks + FloorDiv((frame - Start) * FlicksPerFrame * SpeedPercent, 100)</c>.
    /// </summary>
    /// <param name="c">The media clip.</param>
    /// <param name="frame">The absolute timeline frame.</param>
    /// <param name="r">The sequence rate.</param>
    /// <returns>The source position in flicks.</returns>
    public static long SourceFlicksAt(MediaClip c, long frame, FrameRate r) =>
        c.SourceInFlicks + (long)FloorDiv((Int128)(frame - c.Start) * r.FlicksPerFrame * c.SpeedPercent, 100);

    /// <summary>Converts exact seconds to microseconds, flooring.</summary>
    /// <param name="seconds">The exact number of seconds.</param>
    /// <returns><c>floor(seconds * 1,000,000)</c>.</returns>
    public static long FloorMicros(Rational seconds) =>
        (long)FloorDiv((Int128)seconds.Num * MicrosPerSecond, seconds.Den);

    /// <summary>The flick offset of a timeline frame: <c>frame * FlicksPerFrame</c>.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="r">The sequence rate.</param>
    /// <returns>The position in flicks.</returns>
    public static long FlicksAtFrame(long frame, FrameRate r) => checked(frame * r.FlicksPerFrame);

    /// <summary>The exact start time in seconds of a timeline frame: <c>frame * den / num</c>.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="r">The sequence rate.</param>
    /// <returns>The exact time in seconds.</returns>
    public static Rational SecondsAt(long frame, FrameRate r) =>
        Rational.Create(checked(frame * r.Value.Den), r.Value.Num);

    /// <summary>Converts a stream timestamp to flicks, flooring: <c>floor(ticks * timeBase * FlicksPerSecond)</c>.</summary>
    /// <param name="ticks">The timestamp in units of <paramref name="timeBase"/>.</param>
    /// <param name="timeBase">The stream time base, for example 1/90000.</param>
    /// <returns>The time in flicks.</returns>
    public static long FlicksFromTicks(long ticks, Rational timeBase) =>
        (long)FloorDiv((Int128)ticks * timeBase.Num * FlicksPerSecond, timeBase.Den);

    /// <summary>Converts microseconds to flicks, flooring.</summary>
    /// <param name="micros">The time in microseconds.</param>
    /// <returns>The time in flicks.</returns>
    public static long FlicksFromMicros(long micros) =>
        (long)FloorDiv((Int128)micros * FlicksPerSecond, MicrosPerSecond);

    /// <summary>Converts flicks to microseconds, flooring (seeks are floored to the microsecond).</summary>
    /// <param name="flicks">The time in flicks.</param>
    /// <returns>The time in microseconds.</returns>
    public static long FloorMicrosFromFlicks(long flicks) =>
        (long)FloorDiv((Int128)flicks * MicrosPerSecond, FlicksPerSecond);

    /// <summary>Integer division rounding towards negative infinity.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor. Must not be zero.</param>
    /// <returns><c>floor(a / b)</c>.</returns>
    public static long FloorDiv(long a, long b) => (long)FloorDiv((Int128)a, b);

    /// <summary>128-bit integer division rounding towards negative infinity.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor. Must not be zero.</param>
    /// <returns><c>floor(a / b)</c>.</returns>
    public static Int128 FloorDiv(Int128 a, Int128 b)
    {
        var q = a / b;
        if (a % b != 0 && ((a < 0) != (b < 0)))
        {
            q--;
        }

        return q;
    }
}
