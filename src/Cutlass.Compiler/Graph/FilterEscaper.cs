using System.Text;

namespace Cutlass.Compiler.Graph;

/// <summary>
/// Escaping rules for filtergraphs (TECH_BRIEF 5.5). Graphs refer to files only by bare names
/// (<c>^[A-Za-z0-9_.-]+$</c>), and title text travels in a <c>textfile</c> with <c>expansion=none</c>, so user text
/// never needs escaping inside a graph. What remains is validating names, quoting expressions and the general
/// two-level value escaping for completeness.
/// </summary>
public static class FilterEscaper
{
    /// <summary>True when <paramref name="name"/> is a bare file name usable inside a graph.</summary>
    /// <param name="name">The candidate.</param>
    /// <returns>True for names made only of ASCII letters, digits, <c>_</c>, <c>.</c> and <c>-</c>, other than
    /// <c>.</c> and <c>..</c>.</returns>
    public static bool IsBareName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name is "." or "..")
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not '_' and not '.' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns <paramref name="name"/> when it is a bare name, else throws.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The same name.</returns>
    /// <exception cref="ArgumentException">When the name is not bare (a path, a drive colon, an apostrophe, a space,
    /// non-ASCII text and so on).</exception>
    public static string RequireBareName(string name)
    {
        if (!IsBareName(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a bare file name ([A-Za-z0-9_.-]+). Graphs refer to work directory files only.",
                nameof(name));
        }

        return name;
    }

    /// <summary>
    /// Quotes an expression for a filter option, for example <c>volume='pow(10,(...)/20)'</c>, so its commas and
    /// colons are not taken as separators.
    /// </summary>
    /// <param name="expression">The expression. It must not contain <c>'</c> or <c>\</c>.</param>
    /// <returns>The quoted expression.</returns>
    /// <exception cref="ArgumentException">When the expression contains a quote or backslash.</exception>
    public static string QuoteExpression(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        if (expression.Contains('\'', StringComparison.Ordinal) || expression.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException("An expression cannot contain quotes or backslashes.", nameof(expression));
        }

        return "'" + expression + "'";
    }

    /// <summary>
    /// Escapes arbitrary text as one option value inside a filtergraph, using ffmpeg's two levels: first the option
    /// level (<c>\</c>, <c>'</c>, <c>:</c> and leading or trailing whitespace get a backslash), then the graph level
    /// (<c>\</c>, <c>'</c>, <c>[</c>, <c>]</c>, <c>,</c> and <c>;</c> get a backslash).
    /// </summary>
    /// <remarks>Cutlass itself never puts user text into a graph (titles use <c>textfile</c>); this exists for values
    /// that cannot be files and is covered by tests.</remarks>
    /// <param name="value">The raw value.</param>
    /// <returns>The escaped value.</returns>
    public static string EscapeOptionValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var level1 = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var edgeSpace = char.IsWhiteSpace(c) && (i == 0 || i == value.Length - 1);
            if (c is '\\' or '\'' or ':' || edgeSpace)
            {
                level1.Append('\\');
            }

            level1.Append(c);
        }

        var level2 = new StringBuilder(level1.Length + 8);
        foreach (var c in level1.ToString())
        {
            if (c is '\\' or '\'' or '[' or ']' or ',' or ';')
            {
                level2.Append('\\');
            }

            level2.Append(c);
        }

        return level2.ToString();
    }

    /// <summary>
    /// Normalises title text for a drawtext <c>textfile</c> exactly like the work directory does: <c>\r\n</c> and
    /// <c>\r</c> become <c>\n</c>, and trailing newlines are removed (drawtext would draw an empty last line). Colons,
    /// quotes, backslashes, commas and percent signs stay literal because the file is read with
    /// <c>expansion=none</c>.
    /// </summary>
    /// <param name="text">The title text.</param>
    /// <returns>The file content.</returns>
    public static string NormalizeTitleText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
    }
}
