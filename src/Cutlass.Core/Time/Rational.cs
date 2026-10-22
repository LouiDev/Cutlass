using System.Globalization;

namespace Cutlass.Core.Time;

/// <summary>
/// An exact rational number, always normalized: <see cref="Den"/> is positive and the fraction is reduced.
/// Used for frame rates, time bases, aspect ratios and exact second values.
/// </summary>
/// <remarks>
/// The constructor normalizes, so <c>new Rational(2, 4)</c> equals <c>new Rational(1, 2)</c> and record equality is
/// value equality. <c>default(Rational)</c> has a zero denominator and is not a valid value; use <see cref="Zero"/>.
/// </remarks>
/// <param name="Num">The numerator.</param>
/// <param name="Den">The denominator. Must not be zero.</param>
public readonly record struct Rational(long Num, long Den) : IComparable<Rational>
{
    /// <summary>The value 0/1.</summary>
    public static readonly Rational Zero = new(0, 1);

    /// <summary>The value 1/1.</summary>
    public static readonly Rational One = new(1, 1);

    /// <summary>The normalized numerator (carries the sign).</summary>
    public long Num { get; } = Normalize(Num, Den).Num;

    /// <summary>The normalized denominator, always greater than zero for a constructed value.</summary>
    public long Den { get; } = Normalize(Num, Den).Den;

    /// <summary>True when the value is strictly greater than zero (and was constructed, not defaulted).</summary>
    public bool IsPositive => Num > 0 && Den > 0;

    /// <summary>Creates a normalized rational.</summary>
    /// <param name="n">The numerator.</param>
    /// <param name="d">The denominator. Must not be zero.</param>
    /// <returns>The normalized value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">When <paramref name="d"/> is zero.</exception>
    public static Rational Create(long n, long d) => new(n, d);

    /// <summary>
    /// Parses <c>"30000/1001"</c>, <c>"30"</c> or <c>"1:1"</c> with invariant culture.
    /// </summary>
    /// <param name="s">The text to parse.</param>
    /// <returns>The normalized value.</returns>
    /// <exception cref="FormatException">When the text is not a valid rational or the denominator is zero.</exception>
    public static Rational Parse(string s)
    {
        if (!TryParse(s, out var value))
        {
            throw new FormatException($"'{s}' is not a valid rational number.");
        }

        return value;
    }

    /// <summary>
    /// Tries to parse <c>"30000/1001"</c>, <c>"30"</c> or <c>"1:1"</c> with invariant culture.
    /// A zero denominator (for example ffprobe's <c>"0/0"</c>) is rejected.
    /// </summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="value">The parsed value, or <see cref="Zero"/> on failure.</param>
    /// <returns>True when the text was parsed.</returns>
    public static bool TryParse(string? s, out Rational value)
    {
        value = Zero;
        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        var text = s.AsSpan().Trim();
        var sep = text.IndexOfAny('/', ':');
        long n;
        long d = 1;
        if (sep < 0)
        {
            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n))
            {
                return false;
            }
        }
        else
        {
            if (!long.TryParse(text[..sep], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out n)
                || !long.TryParse(text[(sep + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out d))
            {
                return false;
            }
        }

        if (d == 0 || n == long.MinValue || d == long.MinValue)
        {
            return false;
        }

        value = new Rational(n, d);
        return true;
    }

    /// <summary>Converts to the nearest double (for display and heuristics only, never for time math).</summary>
    /// <returns>The approximate value.</returns>
    public double ToDouble() => (double)Num / Den;

    /// <summary>Formats as ffmpeg expects: <c>"30"</c> when the denominator is 1, else <c>"30000/1001"</c>.</summary>
    /// <returns>The invariant-culture text.</returns>
    public string ToFfString() => Den == 1
        ? Num.ToString(CultureInfo.InvariantCulture)
        : string.Create(CultureInfo.InvariantCulture, $"{Num}/{Den}");

    /// <summary>Compares exactly using 128-bit cross multiplication.</summary>
    /// <param name="o">The other value.</param>
    /// <returns>Negative, zero or positive, like <see cref="IComparable{T}.CompareTo"/>.</returns>
    public int CompareTo(Rational o) => ((Int128)Num * o.Den).CompareTo((Int128)o.Num * Den);

    /// <inheritdoc />
    public override string ToString() => ToFfString();

    /// <summary>Exact less-than comparison.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is smaller.</returns>
    public static bool operator <(Rational left, Rational right) => left.CompareTo(right) < 0;

    /// <summary>Exact greater-than comparison.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is larger.</returns>
    public static bool operator >(Rational left, Rational right) => left.CompareTo(right) > 0;

    /// <summary>Exact less-than-or-equal comparison.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is smaller or equal.</returns>
    public static bool operator <=(Rational left, Rational right) => left.CompareTo(right) <= 0;

    /// <summary>Exact greater-than-or-equal comparison.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when <paramref name="left"/> is larger or equal.</returns>
    public static bool operator >=(Rational left, Rational right) => left.CompareTo(right) >= 0;

    private static (long Num, long Den) Normalize(long num, long den)
    {
        if (den == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(den), "The denominator of a rational must not be zero.");
        }

        if (den < 0)
        {
            num = checked(-num);
            den = checked(-den);
        }

        var g = Gcd(num, den);
        return (num / g, den / g);
    }

    private static long Gcd(long a, long b)
    {
        // b is positive here, so the result is at least 1.
        var x = a < 0 ? (ulong)(-(a + 1)) + 1UL : (ulong)a;
        var y = (ulong)b;
        while (y != 0)
        {
            (x, y) = (y, x % y);
        }

        return (long)x;
    }
}
