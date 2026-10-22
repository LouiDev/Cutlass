namespace Cutlass.Media.Parsing;

/// <summary>Parses the <c>-filters</c> and <c>-encoders</c> listings (TECH_BRIEF 5.2).</summary>
public static class CapabilityListParser
{
    private static readonly char[] Whitespace = [' ', '\t'];

    /// <summary>
    /// Reads filter names. A filter line is one whose first token contains only <c>T</c>, <c>S</c>, <c>C</c> and
    /// <c>.</c> and whose third token contains <c>-&gt;</c>; the name is the second token.
    /// </summary>
    /// <param name="text">The stdout of <c>ffmpeg -hide_banner -filters</c>.</param>
    /// <returns>The filter names, in listing order, without duplicates.</returns>
    public static IReadOnlyList<string> ParseFilters(string? text)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in Lines(text))
        {
            var tokens = line.Split(Whitespace, 4, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3)
            {
                continue;
            }

            var flags = tokens[0];
            if (flags.Length == 0 || !flags.All(c => c is 'T' or 'S' or 'C' or '.'))
            {
                continue;
            }

            if (!tokens[2].Contains("->", StringComparison.Ordinal))
            {
                continue;
            }

            if (seen.Add(tokens[1]))
            {
                names.Add(tokens[1]);
            }
        }

        return names;
    }

    /// <summary>
    /// Reads encoder names: every line after the <c>------</c> separator whose first token is a six-character flag
    /// field starting with <c>V</c>, <c>A</c> or <c>S</c>; the name is the second token.
    /// </summary>
    /// <param name="text">The stdout of <c>ffmpeg -hide_banner -encoders</c>.</param>
    /// <returns>The encoder names, in listing order, without duplicates.</returns>
    public static IReadOnlyList<string> ParseEncoders(string? text)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pastSeparator = false;
        foreach (var line in Lines(text))
        {
            var trimmed = line.Trim();
            if (!pastSeparator)
            {
                pastSeparator = trimmed.Length >= 3 && trimmed.All(c => c == '-');
                continue;
            }

            var tokens = trimmed.Split(Whitespace, 3, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                continue;
            }

            var flags = tokens[0];
            if (flags.Length != 6 || flags[0] is not ('V' or 'A' or 'S') || !flags.All(c => c == '.' || char.IsAsciiLetterUpper(c)))
            {
                continue;
            }

            if (seen.Add(tokens[1]))
            {
                names.Add(tokens[1]);
            }
        }

        return names;
    }

    private static IEnumerable<string> Lines(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.Split('\n').Select(l => l.TrimEnd('\r'));
}
