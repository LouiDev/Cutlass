using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// A minimal TOML reader and writer for one table of a configuration file (AGENT_INTEGRATION 9.3), enough for an MCP
/// server entry such as <c>[mcp_servers.cutlass]</c> with <c>command</c> and <c>args</c>. Setting a key replaces only
/// that key's line inside the table (other keys of the table, other tables, comments and blank lines stay byte for
/// byte); a missing table is appended at the end. Removing drops the table and its sub-tables. Values are strings,
/// booleans, numbers, arrays and inline tables. Pure; no I/O.
/// </summary>
public static class TomlTableEditor
{
    /// <summary>Reads the keys of a table, or null when the table is missing.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="table">The table's dotted key parts, for example <c>mcp_servers</c>, <c>cutlass</c>.</param>
    /// <returns>The keys and values, or null.</returns>
    /// <exception cref="FormatException">A value in the table cannot be read.</exception>
    public static JsonObject? Read(string text, IReadOnlyList<string> table)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(table);
        var lines = SplitLines(text);
        var header = FindHeader(lines, table);
        if (header < 0)
        {
            return null;
        }

        var end = NextHeader(lines, header + 1);
        var result = new JsonObject();
        for (var i = header + 1; i < end; i++)
        {
            if (KeyOf(lines[i].Content) is not { } key)
            {
                continue;
            }

            var (valueText, last) = ValueText(lines, i, end);
            result[key] = ParseValue(valueText.Trim());
            i = last;
        }

        return result;
    }

    /// <summary>
    /// Sets keys of a table: an existing key's line (or lines, for a multi-line array) is replaced, a missing key is
    /// added after the table's last key, and a missing table is appended at the end of the file.
    /// </summary>
    /// <param name="text">The file's text (empty for a missing file).</param>
    /// <param name="table">The table's dotted key parts.</param>
    /// <param name="values">The keys to set; null values are skipped.</param>
    /// <returns>The new text.</returns>
    public static string Upsert(string text, IReadOnlyList<string> table, JsonObject values)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(values);
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = SplitLines(text);
        var header = FindHeader(lines, table);
        if (header < 0)
        {
            var sb = new StringBuilder(text);
            if (text.Length > 0 && !text.EndsWith('\n'))
            {
                sb.Append(nl);
            }

            if (text.Trim().Length > 0)
            {
                sb.Append(nl);
            }

            sb.Append(HeaderText(table)).Append(nl);
            foreach (var (key, value) in values)
            {
                if (value is not null)
                {
                    sb.Append(KeyText(key)).Append(" = ").Append(Format(value)).Append(nl);
                }
            }

            return sb.ToString();
        }

        var end = NextHeader(lines, header + 1);
        var edits = new List<(int First, int Last, string? Replacement)>();
        var lastKeyLine = header;
        var present = new HashSet<string>(StringComparer.Ordinal);
        for (var i = header + 1; i < end; i++)
        {
            if (KeyOf(lines[i].Content) is not { } key)
            {
                continue;
            }

            var (_, last) = ValueText(lines, i, end);
            lastKeyLine = last;
            if (values.TryGetPropertyValue(key, out var value) && value is not null)
            {
                present.Add(key);
                edits.Add((i, last, KeyText(key) + " = " + Format(value) + lines[last].Ending));
            }

            i = last;
        }

        var added = new StringBuilder();
        foreach (var (key, value) in values)
        {
            if (value is not null && !present.Contains(key))
            {
                added.Append(KeyText(key)).Append(" = ").Append(Format(value)).Append(nl);
            }
        }

        var output = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var edit = edits.FirstOrDefault(e => e.First == i);
            if (edit.Replacement is not null)
            {
                output.Append(edit.Replacement);
                i = edit.Last;
            }
            else
            {
                output.Append(lines[i].Content).Append(lines[i].Ending);
            }

            if (i == lastKeyLine && added.Length > 0)
            {
                if (lines[i].Ending.Length == 0)
                {
                    output.Append(nl);
                }

                output.Append(added);
            }
        }

        return output.ToString();
    }

    /// <summary>Removes a table, its keys and its sub-tables; a missing table leaves the text as it is.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="table">The table's dotted key parts.</param>
    /// <param name="removed">True when the table was there.</param>
    /// <returns>The new text.</returns>
    public static string Remove(string text, IReadOnlyList<string> table, out bool removed)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(table);
        var lines = SplitLines(text);
        var drop = new bool[lines.Count];
        removed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (HeaderOf(lines[i].Content) is not { } parts || parts.Count < table.Count || !parts.Take(table.Count).SequenceEqual(table, StringComparer.Ordinal))
            {
                continue;
            }

            removed = true;
            var end = NextHeader(lines, i + 1);
            for (var j = i; j < end; j++)
            {
                drop[j] = true;
            }

            // At the end of the file the blank line that separated the table goes with it.
            if (end == lines.Count && i > 0 && lines[i - 1].Content.Trim().Length == 0 && !drop[i - 1])
            {
                drop[i - 1] = true;
            }

            i = end - 1;
        }

        if (!removed)
        {
            return text;
        }

        var output = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!drop[i])
            {
                output.Append(lines[i].Content).Append(lines[i].Ending);
            }
        }

        return output.ToString();
    }

    /// <summary>Formats a value as TOML: a basic string, a boolean, a number, an array or an inline table.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string Format(JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            JsonArray array => "[" + string.Join(", ", array.Where(e => e is not null).Select(e => Format(e!))) + "]",
            JsonObject obj => obj.Count == 0
                ? "{}"
                : "{ " + string.Join(", ", obj.Where(p => p.Value is not null).Select(p => KeyText(p.Key) + " = " + Format(p.Value!))) + " }",
            JsonValue v when v.TryGetValue<string>(out var s) => BasicString(s),
            JsonValue v when v.TryGetValue<bool>(out var b) => b ? "true" : "false",
            _ => value.ToJsonString(),
        };
    }

    /// <summary>The header line of a table, for example <c>[mcp_servers.cutlass]</c>.</summary>
    /// <param name="table">The table's dotted key parts.</param>
    /// <returns>The header.</returns>
    public static string HeaderText(IReadOnlyList<string> table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return "[" + string.Join('.', table.Select(KeyText)) + "]";
    }

    private static string KeyText(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? key : BasicString(key);

    private static string BasicString(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case < ' ':
                    sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }

    private static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            if (nl < 0)
            {
                lines.Add(new Line(text[start..], string.Empty));
                break;
            }

            var contentEnd = nl > start && text[nl - 1] == '\r' ? nl - 1 : nl;
            lines.Add(new Line(text[start..contentEnd], text[contentEnd..(nl + 1)]));
            start = nl + 1;
        }

        return lines;
    }

    private static int FindHeader(List<Line> lines, IReadOnlyList<string> table)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (HeaderOf(lines[i].Content) is { } parts && parts.SequenceEqual(table, StringComparer.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static int NextHeader(List<Line> lines, int from)
    {
        for (var i = from; i < lines.Count; i++)
        {
            if (lines[i].Content.TrimStart().StartsWith('['))
            {
                // A line inside a multi-line array can also start with '[' only when nested arrays are written one per
                // line; a header is a '[' line that closes on the same line and has no '=' before its ']'.
                var t = lines[i].Content.Trim();
                if (t.StartsWith("[[", StringComparison.Ordinal) || HeaderOf(t) is not null)
                {
                    return i;
                }
            }
        }

        return lines.Count;
    }

    // "[a.b]" or "[ a . "b c" ]" with an optional comment; null for other lines and for array tables.
    private static List<string>? HeaderOf(string line)
    {
        var t = line.Trim();
        if (!t.StartsWith('[') || t.StartsWith("[[", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = new List<string>();
        var current = new StringBuilder();
        var i = 1;
        while (i < t.Length)
        {
            var c = t[i];
            if (c == ']')
            {
                var rest = t[(i + 1)..].Trim();
                if (rest.Length > 0 && !rest.StartsWith('#'))
                {
                    return null;
                }

                parts.Add(current.ToString().Trim());
                return parts.Any(p => p.Length == 0) ? null : parts;
            }

            if (c is '"' or '\'')
            {
                var close = t.IndexOf(c, i + 1);
                if (close < 0)
                {
                    return null;
                }

                current.Append(t, i + 1, close - i - 1);
                i = close + 1;
                continue;
            }

            if (c == '.')
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
            }
            else if (c == '=')
            {
                return null;
            }
            else if (!char.IsWhiteSpace(c))
            {
                current.Append(c);
            }

            i++;
        }

        return null;
    }

    private static string? KeyOf(string line)
    {
        var t = line.TrimStart();
        if (t.Length == 0 || t[0] is '#' or '[')
        {
            return null;
        }

        var eq = t.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0)
        {
            return null;
        }

        var key = t[..eq].Trim();
        return key.Length >= 2 && key[0] is '"' or '\'' && key[^1] == key[0] ? key[1..^1] : key;
    }

    // The value text of the key on line i, joined over the following lines while an array or inline table is open.
    private static (string Text, int Last) ValueText(List<Line> lines, int i, int end)
    {
        var line = lines[i].Content;
        var text = new StringBuilder(line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..]);
        var last = i;
        while (Depth(text.ToString()) > 0 && last + 1 < end)
        {
            last++;
            text.Append('\n').Append(lines[last].Content);
        }

        return (text.ToString(), last);
    }

    // Open brackets and braces outside strings and comments.
    private static int Depth(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '"' or '\'')
            {
                i = SkipString(text, i);
            }
            else if (c == '#')
            {
                var nl = text.IndexOf('\n', i);
                if (nl < 0)
                {
                    break;
                }

                i = nl;
            }
            else if (c is '[' or '{')
            {
                depth++;
            }
            else if (c is ']' or '}')
            {
                depth--;
            }
        }

        return depth;
    }

    private static int SkipString(string text, int start)
    {
        var quote = text[start];
        for (var i = start + 1; i < text.Length; i++)
        {
            if (quote == '"' && text[i] == '\\')
            {
                i++;
            }
            else if (text[i] == quote)
            {
                return i;
            }
        }

        return text.Length;
    }

    private static JsonNode? ParseValue(string text)
    {
        var reader = new ValueReader(text);
        var value = reader.Read();
        return value;
    }

    private readonly record struct Line(string Content, string Ending);

    private sealed class ValueReader(string text)
    {
        private int _i;

        public JsonNode? Read()
        {
            Skip();
            if (_i >= text.Length)
            {
                throw new FormatException("A TOML key has no value.");
            }

            var c = text[_i];
            if (c == '"')
            {
                return JsonValue.Create(Basic());
            }

            if (c == '\'')
            {
                var close = text.IndexOf('\'', _i + 1);
                if (close < 0)
                {
                    throw new FormatException("Unterminated TOML string.");
                }

                var s = text[(_i + 1)..close];
                _i = close + 1;
                return JsonValue.Create(s);
            }

            if (c == '[')
            {
                _i++;
                var array = new JsonArray();
                while (true)
                {
                    Skip();
                    if (_i >= text.Length)
                    {
                        throw new FormatException("Unterminated TOML array.");
                    }

                    if (text[_i] == ']')
                    {
                        _i++;
                        return array;
                    }

                    if (text[_i] == ',')
                    {
                        _i++;
                        continue;
                    }

                    array.Add(Read());
                }
            }

            if (c == '{')
            {
                _i++;
                var obj = new JsonObject();
                while (true)
                {
                    Skip();
                    if (_i >= text.Length)
                    {
                        throw new FormatException("Unterminated TOML inline table.");
                    }

                    if (text[_i] == '}')
                    {
                        _i++;
                        return obj;
                    }

                    if (text[_i] == ',')
                    {
                        _i++;
                        continue;
                    }

                    var eq = text.IndexOf('=', _i);
                    if (eq < 0)
                    {
                        throw new FormatException("A TOML inline table key has no value.");
                    }

                    var key = text[_i..eq].Trim().Trim('"', '\'');
                    _i = eq + 1;
                    obj[key] = Read();
                }
            }

            var start = _i;
            while (_i < text.Length && text[_i] is not (',' or ']' or '}' or '#') && !char.IsWhiteSpace(text[_i]))
            {
                _i++;
            }

            var bare = text[start.._i];
            return bare switch
            {
                "true" => JsonValue.Create(true),
                "false" => JsonValue.Create(false),
                _ when long.TryParse(bare.Replace("_", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) => JsonValue.Create(n),
                _ when double.TryParse(bare.Replace("_", string.Empty, StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
                _ => JsonValue.Create(bare),
            };
        }

        private void Skip()
        {
            while (_i < text.Length)
            {
                if (char.IsWhiteSpace(text[_i]))
                {
                    _i++;
                }
                else if (text[_i] == '#')
                {
                    var nl = text.IndexOf('\n', _i);
                    _i = nl < 0 ? text.Length : nl + 1;
                }
                else
                {
                    return;
                }
            }
        }

        private string Basic()
        {
            var sb = new StringBuilder();
            _i++;
            while (_i < text.Length && text[_i] != '"')
            {
                if (text[_i] == '\\' && _i + 1 < text.Length)
                {
                    var e = text[_i + 1];
                    _i += 2;
                    switch (e)
                    {
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'u' when _i + 4 <= text.Length:
                            sb.Append((char)int.Parse(text.AsSpan(_i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            _i += 4;
                            break;
                        case 'U' when _i + 8 <= text.Length:
                            sb.Append(char.ConvertFromUtf32(int.Parse(text.AsSpan(_i, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                            _i += 8;
                            break;
                        default:
                            sb.Append(e);
                            break;
                    }
                }
                else
                {
                    sb.Append(text[_i]);
                    _i++;
                }
            }

            if (_i >= text.Length)
            {
                throw new FormatException("Unterminated TOML string.");
            }

            _i++;
            return sb.ToString();
        }
    }
}
