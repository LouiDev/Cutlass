using System.Globalization;
using Cutlass.Agent.Api;
using Cutlass.Core.Time;

namespace Cutlass.Agent.Tools;

/// <summary>Argument checks shared by the tools; each failure is an <c>invalid_argument</c> naming the field.</summary>
public static class Args
{
    /// <summary>The longest id array (AGENT_INTEGRATION 4.6).</summary>
    public const int MaxIds = 1000;

    /// <summary>A required value.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="field">The field.</param>
    /// <returns>The value.</returns>
    public static T Required<T>(T? value, string field)
        where T : struct =>
        value ?? throw ApiException.InvalidArgument(field, $"'{field}' is required.");

    /// <summary>A required non-empty string.</summary>
    /// <param name="value">The value.</param>
    /// <param name="field">The field.</param>
    /// <returns>The value.</returns>
    public static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw ApiException.InvalidArgument(field, $"'{field}' is required.") : value;

    /// <summary>A required, non-empty id list of at most 1000 ids.</summary>
    /// <param name="ids">The ids.</param>
    /// <param name="field">The field.</param>
    /// <returns>The distinct ids.</returns>
    public static IReadOnlyList<string> Ids(List<string>? ids, string field)
    {
        if (ids is null || ids.Count == 0)
        {
            throw ApiException.InvalidArgument(field, $"'{field}' needs at least one id.");
        }

        return OptionalIds(ids, field)!;
    }

    /// <summary>An optional id list of at most 1000 ids.</summary>
    /// <param name="ids">The ids, or null.</param>
    /// <param name="field">The field.</param>
    /// <returns>The distinct ids, or null.</returns>
    public static IReadOnlyList<string>? OptionalIds(List<string>? ids, string field)
    {
        if (ids is null)
        {
            return null;
        }

        if (ids.Count > MaxIds)
        {
            throw new ApiException(ErrorCode.PayloadTooLarge, $"'{field}' holds {ids.Count} ids; at most {MaxIds} are allowed per call.",
                new Dictionary<string, string> { ["field"] = field });
        }

        return [.. ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>A value within bounds.</summary>
    /// <param name="value">The value.</param>
    /// <param name="min">The minimum.</param>
    /// <param name="max">The maximum.</param>
    /// <param name="field">The field.</param>
    /// <returns>The value.</returns>
    public static long InRange(long value, long min, long max, string field) =>
        value < min || value > max
            ? throw ApiException.InvalidArgument(field, string.Create(CultureInfo.InvariantCulture, $"'{field}' must be between {min} and {max}; it was {value}."))
            : value;

    /// <summary>A number within bounds.</summary>
    /// <param name="value">The value.</param>
    /// <param name="min">The minimum.</param>
    /// <param name="max">The maximum.</param>
    /// <param name="field">The field.</param>
    /// <returns>The value.</returns>
    public static double InRange(double value, double min, double max, string field) =>
        double.IsNaN(value) || value < min || value > max
            ? throw ApiException.InvalidArgument(field, string.Create(CultureInfo.InvariantCulture, $"'{field}' must be between {min} and {max}; it was {value}."))
            : value;

    /// <summary>
    /// A source time given as flicks or as seconds (rounded to the nearest flick), never both (AGENT_INTEGRATION 4.2).
    /// </summary>
    /// <param name="flicks">The flicks, or null.</param>
    /// <param name="seconds">The seconds, or null.</param>
    /// <param name="field">The flicks field name, for example <c>sourceInFlicks</c>.</param>
    /// <returns>The flicks, or null when neither was given.</returns>
    public static long? Flicks(long? flicks, double? seconds, string field)
    {
        if (flicks is not null && seconds is not null)
        {
            var secondsField = field.EndsWith("Flicks", StringComparison.Ordinal) ? field[..^6] + "Seconds" : field + "Seconds";
            throw ApiException.InvalidArgument(field, $"Give '{field}' or '{secondsField}', not both.");
        }

        if (seconds is { } s)
        {
            if (double.IsNaN(s) || double.IsInfinity(s) || Math.Abs(s) > 1e9)
            {
                throw ApiException.InvalidArgument(field, $"'{field}' in seconds is not a valid time.");
            }

            return (long)Math.Round(s * TimeMath.FlicksPerSecond, MidpointRounding.AwayFromZero);
        }

        return flicks;
    }

    /// <summary>A colour <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="field">The field.</param>
    /// <param name="withAlpha">True to return <c>#RRGGBBAA</c> (FF added when missing).</param>
    /// <returns>The colour in upper case.</returns>
    public static string Color(string value, string field, bool withAlpha)
    {
        var ok = value.Length is 7 or 9 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
        if (!ok)
        {
            throw ApiException.InvalidArgument(field, $"'{field}' must be #RRGGBB or #RRGGBBAA; it was '{value}'.");
        }

        var upper = value.ToUpperInvariant();
        return withAlpha ? (upper.Length == 7 ? upper + "FF" : upper) : upper[..7];
    }

    /// <summary>Parses a frame rate such as <c>30000/1001</c>, <c>29.97</c> or <c>25</c> to the nearest supported rate.</summary>
    /// <param name="text">The text.</param>
    /// <param name="field">The field.</param>
    /// <returns>The rate.</returns>
    public static FrameRate Rate(string text, string field)
    {
        if (Rational.TryParse(text, out var r) && r.IsPositive)
        {
            return Exact(FrameRate.Nearest(r), r, text, field);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0 && d < 1000)
        {
            var approx = new Rational((long)Math.Round(d * 1000), 1000);
            var nearest = FrameRate.Nearest(approx);
            return Math.Abs(nearest.Value.ToDouble() - d) < 0.01 ? nearest : throw Unsupported(text, field);
        }

        throw Unsupported(text, field);
    }

    private static FrameRate Exact(FrameRate nearest, Rational r, string text, string field) =>
        Math.Abs(nearest.Value.ToDouble() - r.ToDouble()) < 0.001 ? nearest : throw Unsupported(text, field);

    private static ApiException Unsupported(string text, string field) =>
        ApiException.InvalidArgument(
            field,
            $"'{text}' is not a supported frame rate. Use one of: {string.Join(", ", FrameRate.Supported.Select(r => r.ToString()))}.");
}
