using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// Edits one member of a JSON configuration file in place (AGENT_INTEGRATION 9.3): the file is scanned for the spans
/// of its members (comments and trailing commas allowed, as many clients write them), and only the span of the
/// edited member changes, so every other byte (other servers, comments, key order, indentation, line ends) stays as
/// it was. A new member gets the file's indentation unit and line end. Pure; no I/O.
/// </summary>
public static class JsoncEditor
{
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly JsonWriterOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Reads the value at a path, or null when a key on the path is missing.</summary>
    /// <param name="text">The file's text (empty for a missing file).</param>
    /// <param name="path">The keys from the root.</param>
    /// <returns>The value, or null.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static JsonNode? Read(string text, IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        JsonNode? node = JsonNode.Parse(text, documentOptions: ReadOptions);
        foreach (var key in path)
        {
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(key, out var child))
            {
                return null;
            }

            node = child;
        }

        return node;
    }

    /// <summary>
    /// Sets the value at a path: the existing value's span is replaced, a missing key is added after the last member
    /// of its object (or into an empty one), and missing objects on the way are created. An empty text becomes a new
    /// document.
    /// </summary>
    /// <param name="text">The file's text (empty for a missing file).</param>
    /// <param name="path">The keys from the root (at least one).</param>
    /// <param name="value">The value.</param>
    /// <returns>The new text.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    /// <exception cref="FormatException">The root is not an object.</exception>
    public static string Upsert(string text, IReadOnlyList<string> path, JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);
        if (path.Count == 0)
        {
            throw new ArgumentException("The path needs at least one key.", nameof(path));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return Pretty(Nest(path, 0, value), string.Empty, "  ", "\n") + "\n";
        }

        var root = Scan(text);
        var unit = IndentUnit(text);
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var node = root;
        for (var k = 0; k < path.Count; k++)
        {
            var member = node.Members.LastOrDefault(m => string.Equals(m.Key, path[k], StringComparison.Ordinal));
            if (member is null)
            {
                return Insert(text, node, path[k], Nest(path, k + 1, value), unit, nl);
            }

            if (k == path.Count - 1 || member.Object is null)
            {
                var replacement = Pretty(Nest(path, k + 1, value), LineIndent(text, member.KeyStart), unit, nl);
                return string.Concat(text.AsSpan(0, member.ValueStart), replacement, text.AsSpan(member.ValueEnd));
            }

            node = member.Object;
        }

        return text;
    }

    /// <summary>Removes the member at a path with its comma; nothing else changes. A missing member leaves the text as it is.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="path">The keys from the root.</param>
    /// <param name="removed">True when a member was removed.</param>
    /// <returns>The new text.</returns>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static string Remove(string text, IReadOnlyList<string> path, out bool removed)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(path);
        removed = false;
        if (string.IsNullOrWhiteSpace(text) || path.Count == 0)
        {
            return text;
        }

        var node = Scan(text);
        for (var k = 0; k < path.Count; k++)
        {
            var index = node.Members.FindLastIndex(m => string.Equals(m.Key, path[k], StringComparison.Ordinal));
            if (index < 0)
            {
                return text;
            }

            var member = node.Members[index];
            if (k < path.Count - 1)
            {
                if (member.Object is null)
                {
                    return text;
                }

                node = member.Object;
                continue;
            }

            removed = true;
            if (member.Comma >= 0)
            {
                return string.Concat(text.AsSpan(0, member.LeadStart), text.AsSpan(member.Comma + 1));
            }

            if (index > 0 && node.Members[index - 1].Comma >= 0)
            {
                return string.Concat(text.AsSpan(0, node.Members[index - 1].Comma), text.AsSpan(member.ValueEnd));
            }

            return string.Concat(text.AsSpan(0, node.Open + 1), text.AsSpan(node.Close));
        }

        return text;
    }

    /// <summary>Formats a value the way a person indents JSON: objects and nested arrays on their own lines, short arrays inline.</summary>
    /// <param name="node">The value.</param>
    /// <param name="indent">The indentation of the line the value starts on.</param>
    /// <param name="unit">One indentation level.</param>
    /// <param name="nl">The line end.</param>
    /// <returns>The text.</returns>
    public static string Pretty(JsonNode? node, string indent, string unit, string nl)
    {
        ArgumentNullException.ThrowIfNull(indent);
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(nl);
        switch (node)
        {
            case null:
                return "null";
            case JsonObject obj:
            {
                if (obj.Count == 0)
                {
                    return "{}";
                }

                var sb = new StringBuilder("{").Append(nl);
                var i = 0;
                foreach (var (key, value) in obj)
                {
                    sb.Append(indent).Append(unit).Append(Quote(key)).Append(": ").Append(Pretty(value, indent + unit, unit, nl));
                    sb.Append(++i < obj.Count ? "," : string.Empty).Append(nl);
                }

                return sb.Append(indent).Append('}').ToString();
            }

            case JsonArray array:
            {
                if (array.Count == 0)
                {
                    return "[]";
                }

                if (array.All(e => e is null or JsonValue))
                {
                    return "[" + string.Join(", ", array.Select(e => Pretty(e, indent, unit, nl))) + "]";
                }

                var sb = new StringBuilder("[").Append(nl);
                for (var i = 0; i < array.Count; i++)
                {
                    sb.Append(indent).Append(unit).Append(Pretty(array[i], indent + unit, unit, nl)).Append(i + 1 < array.Count ? "," : string.Empty).Append(nl);
                }

                return sb.Append(indent).Append(']').ToString();
            }

            case JsonValue value when value.TryGetValue<string>(out var s):
                return Quote(s);
            default:
                return node.ToJsonString();
        }
    }

    /// <summary>A JSON string literal with only the escapes JSON needs (paths keep their characters).</summary>
    /// <param name="value">The string.</param>
    /// <returns>The quoted literal.</returns>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, StringOptions))
        {
            writer.WriteStringValue(value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static JsonNode Nest(IReadOnlyList<string> path, int from, JsonNode value)
    {
        if (from >= path.Count)
        {
            return value.DeepClone();
        }

        return new JsonObject { [path[from]] = Nest(path, from + 1, value) };
    }

    private static string Insert(string text, ObjectSpan node, string key, JsonNode value, string unit, string nl)
    {
        var objectIndent = LineIndent(text, node.Open);
        if (node.Members.Count == 0)
        {
            var childIndent = objectIndent + unit;
            var member = Quote(key) + ": " + Pretty(value, childIndent, unit, nl);
            return string.Concat(text.AsSpan(0, node.Open + 1), nl + childIndent + member + nl + objectIndent, text.AsSpan(node.Close));
        }

        var last = node.Members[^1];
        var inline = text.AsSpan(node.Open, last.KeyStart - node.Open).IndexOfAny('\r', '\n') < 0;
        var indent = inline ? objectIndent + unit : LineIndent(text, last.KeyStart);
        var memberText = Quote(key) + ": " + Pretty(value, indent, unit, nl);
        if (last.Comma >= 0)
        {
            // The file uses trailing commas: keep the style.
            var after = inline ? " " + memberText + "," : nl + indent + memberText + ",";
            return string.Concat(text.AsSpan(0, last.Comma + 1), after, text.AsSpan(last.Comma + 1));
        }

        var insertion = inline ? ", " + memberText : "," + nl + indent + memberText;
        return string.Concat(text.AsSpan(0, last.ValueEnd), insertion, text.AsSpan(last.ValueEnd));
    }

    private static string LineIndent(string text, int position)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        if (position == 0)
        {
            start = 0;
        }

        var end = start;
        while (end < text.Length && end < position && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text[start..end];
    }

    private static string IndentUnit(string text)
    {
        foreach (var line in text.Split('\n').Skip(1))
        {
            var trimmed = line.TrimStart(' ', '\t');
            if (trimmed.Length == 0 || trimmed.Length == line.Length || trimmed[0] == '\r')
            {
                continue;
            }

            var lead = line[..(line.Length - trimmed.Length)];
            return lead.Contains('\t', StringComparison.Ordinal) ? "\t" : lead;
        }

        return "  ";
    }

    private static ObjectSpan Scan(string text)
    {
        // Validate first, so a broken file is reported the same way the reader reports it.
        using (JsonDocument.Parse(text, ReadOptions))
        {
        }

        var scanner = new Scanner(text);
        scanner.SkipTrivia();
        if (scanner.Peek() != '{')
        {
            throw new FormatException("The configuration file's root is not a JSON object.");
        }

        return scanner.ParseObject();
    }

    private sealed class ObjectSpan
    {
        public int Open { get; init; }

        public int Close { get; set; }

        public List<MemberSpan> Members { get; } = [];
    }

    private sealed class MemberSpan
    {
        public required string Key { get; init; }

        public int LeadStart { get; init; }

        public int KeyStart { get; init; }

        public int ValueStart { get; init; }

        public int ValueEnd { get; init; }

        public int Comma { get; set; } = -1;

        public ObjectSpan? Object { get; init; }
    }

    private sealed class Scanner(string text)
    {
        private int _i;

        public char Peek() => _i < text.Length ? text[_i] : '\0';

        public void SkipTrivia()
        {
            while (_i < text.Length)
            {
                var c = text[_i];
                if (char.IsWhiteSpace(c))
                {
                    _i++;
                }
                else if (c == '/' && _i + 1 < text.Length && text[_i + 1] == '/')
                {
                    var end = text.IndexOf('\n', _i);
                    _i = end < 0 ? text.Length : end + 1;
                }
                else if (c == '/' && _i + 1 < text.Length && text[_i + 1] == '*')
                {
                    var end = text.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    _i = end < 0 ? text.Length : end + 2;
                }
                else
                {
                    return;
                }
            }
        }

        public ObjectSpan ParseObject()
        {
            var node = new ObjectSpan { Open = _i };
            _i++;
            var lead = _i;
            MemberSpan? previous = null;
            while (true)
            {
                SkipTrivia();
                var c = Peek();
                if (c == '}')
                {
                    node.Close = _i;
                    _i++;
                    return node;
                }

                if (c == ',')
                {
                    // A stray comma (JSONC tolerates them).
                    if (previous is not null && previous.Comma < 0)
                    {
                        previous.Comma = _i;
                    }

                    _i++;
                    lead = _i;
                    continue;
                }

                if (c != '"')
                {
                    throw new FormatException("Expected a key at offset " + _i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                }

                var keyStart = _i;
                var key = ParseString();
                SkipTrivia();
                if (Peek() != ':')
                {
                    throw new FormatException("Expected ':' at offset " + _i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                }

                _i++;
                SkipTrivia();
                var valueStart = _i;
                var child = ParseValue();
                var member = new MemberSpan { Key = key, LeadStart = lead, KeyStart = keyStart, ValueStart = valueStart, ValueEnd = _i, Object = child };
                node.Members.Add(member);
                previous = member;
                SkipTrivia();
                if (Peek() == ',')
                {
                    member.Comma = _i;
                    _i++;
                    lead = _i;
                }
            }
        }

        private ObjectSpan? ParseValue()
        {
            switch (Peek())
            {
                case '{':
                    return ParseObject();
                case '[':
                    ParseArray();
                    return null;
                case '"':
                    ParseString();
                    return null;
                case '\0':
                    throw new FormatException("Unexpected end of the configuration file.");
                default:
                    var start = _i;
                    while (_i < text.Length && text[_i] is not (',' or '}' or ']' or '/') && !char.IsWhiteSpace(text[_i]))
                    {
                        _i++;
                    }

                    if (_i == start)
                    {
                        throw new FormatException("Expected a value at offset " + _i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
                    }

                    return null;
            }
        }

        private void ParseArray()
        {
            _i++;
            while (true)
            {
                SkipTrivia();
                var c = Peek();
                if (c == ']')
                {
                    _i++;
                    return;
                }

                if (c == ',')
                {
                    _i++;
                    continue;
                }

                ParseValue();
            }
        }

        private string ParseString()
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
                            sb.Append((char)int.Parse(text.AsSpan(_i, 4), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
                            _i += 4;
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
                throw new FormatException("Unterminated string in the configuration file.");
            }

            _i++;
            return sb.ToString();
        }
    }
}
