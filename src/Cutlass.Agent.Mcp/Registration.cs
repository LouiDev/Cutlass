using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cutlass.Agent.Mcp;

/// <summary>
/// The registration text of <c>--print-registration</c> (docs/AGENT_INTEGRATION.md 5.5 and 9.4): one block per agent
/// client profile of <c>agent-clients.json</c> (the file the app ships beside the exes; in a checkout the copy in
/// <c>src/Cutlass.App/Assets</c>), filled in with the path of the running exe: the client CLI's command, the entry to
/// add to the client's configuration file (JSON or TOML) with its path on this platform, or a harness's setup text.
/// Without the profile file it prints the generic MCP entry. Run through <c>dotnet</c> (a framework-dependent build),
/// the command is <c>dotnet</c> with the dll as its first argument.
/// </summary>
public static partial class Registration
{
    /// <summary>The server name registered with the clients.</summary>
    public const string ServerName = "cutlass";

    /// <summary>The profile file's name beside the exe.</summary>
    public const string ProfileFileName = "agent-clients.json";

    /// <summary>The default agent server URL the harness texts name.</summary>
    public const string DefaultUrl = "http://localhost:47390/";

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The command and leading arguments that start this exe.</summary>
    /// <param name="processPath">The running process's path (<see cref="Environment.ProcessPath"/>).</param>
    /// <param name="baseDirectory">The folder of this assembly (<see cref="AppContext.BaseDirectory"/>).</param>
    /// <returns>The command and the arguments that come before any option.</returns>
    public static (string Command, IReadOnlyList<string> Args) CurrentCommand(string? processPath, string baseDirectory)
    {
        var dll = Path.Combine(baseDirectory, "Cutlass.Agent.Mcp.dll");
        if (processPath is null || string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return (processPath ?? "dotnet", [dll]);
        }

        return (processPath, []);
    }

    /// <summary>
    /// Finds the profile file: beside the exe (a publish folder), else <c>src/Cutlass.App/Assets</c> of the checkout the
    /// exe was built in.
    /// </summary>
    /// <param name="baseDirectory">The folder of this assembly.</param>
    /// <returns>The file, or null.</returns>
    public static string? FindProfileFile(string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(baseDirectory);
        var beside = Path.Combine(baseDirectory, ProfileFileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        var folder = new DirectoryInfo(baseDirectory);
        for (var i = 0; i < 8 && folder is not null; i++, folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "src", "Cutlass.App", "Assets", ProfileFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds the registration text of every profile. Placeholders: <c>{exe}</c> (the command; through <c>dotnet</c>
    /// the dll follows it), <c>{scope}</c> (the profile's first scope), <c>{home}</c>, <c>{appdata}</c>, <c>{url}</c>
    /// (<see cref="DefaultUrl"/>) and <c>{token}</c> (a pointer to the token file; the exe never prints the token).
    /// </summary>
    /// <param name="profilesJson">The profile file's text.</param>
    /// <param name="command">The command.</param>
    /// <param name="args">The leading arguments (the dll when run through <c>dotnet</c>).</param>
    /// <param name="windows">True to quote for Windows shells and use the Windows paths.</param>
    /// <param name="home">The home folder.</param>
    /// <param name="appData">The application data folder (Windows: roaming AppData; elsewhere <c>~/.config</c>).</param>
    /// <param name="platform"><c>windows</c>, <c>macos</c> or <c>linux</c>.</param>
    /// <returns>The text, ending with a newline.</returns>
    /// <exception cref="JsonException">The profile file is not valid.</exception>
    public static string BuildFromProfiles(string profilesJson, string command, IReadOnlyList<string> args, bool windows, string home, string appData, string platform)
    {
        ArgumentNullException.ThrowIfNull(profilesJson);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);
        using var document = JsonDocument.Parse(profilesJson, ReadOptions);
        if (!document.RootElement.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The profile file has no clients array.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["home"] = home,
            ["appdata"] = appData,
            ["url"] = DefaultUrl,
            ["token"] = "the content of the token file (<data>/agent/token; Settings > Agents > Token > Copy)",
        };
        var text = new StringBuilder();
        foreach (var client in clients.EnumerateArray())
        {
            var name = String(client, "name") ?? String(client, "id") ?? "client";
            var http = string.Equals(String(client, "transport"), "http", StringComparison.OrdinalIgnoreCase);
            if (client.TryGetProperty("register", out var register) && String(register, "command") is { } cli)
            {
                var scopes = client.TryGetProperty("scopes", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList() : [];
                values["scope"] = scopes.Count > 0 ? scopes[0] : string.Empty;
                text.Append(name).AppendLine(", MCP over stdio, through its CLI:");
                text.Append("  ").AppendLine(string.Join(' ', new[] { cli }.Concat(ExpandArgs(register, values, command, args)).Select(a => Quote(a, windows))));
                if (scopes.Count > 1)
                {
                    text.Append("  (scopes: ").Append(string.Join(", ", scopes)).AppendLine(")");
                }
            }
            else if (ResolvePath(client, "configFile", platform, values) is { } path && client.TryGetProperty("configEntry", out var entry))
            {
                var toml = string.Equals(String(client, "configFormat"), "toml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase);
                var segments = (String(entry, "path") ?? ServerName).Split('.', StringSplitOptions.RemoveEmptyEntries);
                text.Append(name).Append(http ? ", over HTTP" : ", MCP over stdio").Append(". Add to ").Append(path).AppendLine(":");
                var snippet = entry.TryGetProperty("value", out var value)
                    ? toml ? TomlSnippet(segments, value, values, command, args) : JsonSnippet(segments, value, values, command, args)
                    : string.Empty;
                text.AppendLine(Indent(snippet));
            }
            else if (String(client, "text") is { } setup)
            {
                text.Append(name).AppendLine(":");
                text.AppendLine(Indent(Expand(setup, values)));
            }
            else
            {
                continue;
            }

            text.AppendLine();
        }

        AppendFooter(text, command, args, windows);
        return text.ToString();
    }

    /// <summary>Builds the registration text without a profile file: the generic MCP entry and the headless arguments.</summary>
    /// <param name="command">The command.</param>
    /// <param name="args">The leading arguments (the dll when run through <c>dotnet</c>).</param>
    /// <param name="windows">True to quote for Windows shells.</param>
    /// <returns>The text, ending with a newline.</returns>
    public static string Build(string command, IReadOnlyList<string> args, bool windows)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);
        var text = new StringBuilder();
        text.AppendLine("The agent client profiles (" + ProfileFileName + ") were not found beside this exe. Every MCP client takes this command:");
        text.Append("  ").AppendLine(string.Join(' ', new[] { command }.Concat(args).Select(a => Quote(a, windows))));
        text.AppendLine();
        text.AppendLine("As a JSON entry (the mcpServers shape most clients use):");
        text.AppendLine(Indent(DesktopJson(command, args)));
        text.AppendLine();
        AppendFooter(text, command, args, windows);
        return text.ToString();
    }

    /// <summary>The <c>mcpServers</c> configuration entry for this exe.</summary>
    /// <param name="command">The command.</param>
    /// <param name="args">The leading arguments.</param>
    /// <returns>The indented JSON.</returns>
    public static string DesktopJson(string command, IReadOnlyList<string> args)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteStartObject("mcpServers");
            w.WriteStartObject(ServerName);
            w.WriteString("command", command);
            w.WriteStartArray("args");
            foreach (var arg in args)
            {
                w.WriteStringValue(arg);
            }

            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Quotes a shell argument when it needs it: double quotes on Windows, single quotes elsewhere.</summary>
    /// <param name="value">The argument.</param>
    /// <param name="windows">True for Windows shells.</param>
    /// <returns>The argument as typed in a shell.</returns>
    public static string Quote(string value, bool windows)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 0 && value.All(c => char.IsLetterOrDigit(c) || c is '\\' or '/' or ':' or '.' or '-' or '_'))
        {
            return value;
        }

        return windows ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }

    private static void AppendFooter(StringBuilder text, string command, IReadOnlyList<string> args, bool windows)
    {
        var line = string.Join(' ', new[] { command }.Concat(args).Select(a => Quote(a, windows)));
        var project = windows ? @"D:\Edits\project.cutlass" : "/home/me/edits/project.cutlass";
        var footage = windows ? @"D:\Footage" : "/home/me/footage";
        var renders = windows ? @"D:\Renders" : "/home/me/renders";
        text.AppendLine("Headless (no app; the engine runs in the MCP server on one project file), register a second server named " + ServerName + "-headless with:");
        text.Append("  ").Append(line).Append(" --headless --project ").Append(Quote(project, windows))
            .Append(" --allow-read ").Append(Quote(footage, windows)).Append(" --allow-write ").AppendLine(Quote(renders, windows));
        text.AppendLine();
        text.AppendLine("The default mode proxies to the running Cutlass app: turn on Settings > Agents > Allow agent connections,");
        text.AppendLine("or start Cutlass.App with --agent-server. In the app, Agent > Connect an agent client... registers these for you.");
    }

    private static IEnumerable<string> ExpandArgs(JsonElement command, Dictionary<string, string> values, string exe, IReadOnlyList<string> leading)
    {
        if (!command.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        string? previous = null;
        var pending = new List<string>();
        foreach (var arg in args.EnumerateArray().Select(a => a.GetString() ?? string.Empty))
        {
            if (arg == "{exe}")
            {
                pending.Add(exe);
                pending.AddRange(leading);
                previous = exe;
                continue;
            }

            var expanded = Expand(arg, values);
            if (expanded.Length == 0 && arg.Length > 0)
            {
                // An empty placeholder drops the option before it (for example "-s {scope}" without a scope).
                if (previous is not null && previous.StartsWith('-') && previous != "--" && pending.Count > 0)
                {
                    pending.RemoveAt(pending.Count - 1);
                }

                continue;
            }

            pending.Add(expanded);
            previous = expanded;
        }

        foreach (var arg in pending)
        {
            yield return arg;
        }
    }

    private static string JsonSnippet(IReadOnlyList<string> segments, JsonElement value, Dictionary<string, string> values, string exe, IReadOnlyList<string> leading)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            foreach (var segment in segments)
            {
                w.WriteStartObject(segment);
            }

            WriteEntryMembers(w, value, values, exe, leading);
            for (var i = 0; i < segments.Count; i++)
            {
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // The entry's members; "command": "{exe}" with leading arguments (dotnet + dll) puts them before the profile's args.
    private static void WriteEntryMembers(Utf8JsonWriter w, JsonElement value, Dictionary<string, string> values, string exe, IReadOnlyList<string> leading)
    {
        var exeCommand = value.TryGetProperty("command", out var c) && c.GetString() == "{exe}";
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name == "args" && exeCommand && property.Value.ValueKind == JsonValueKind.Array)
            {
                w.WriteStartArray("args");
                foreach (var arg in leading)
                {
                    w.WriteStringValue(arg);
                }

                foreach (var arg in property.Value.EnumerateArray())
                {
                    WriteValue(w, arg, values, exe);
                }

                w.WriteEndArray();
                continue;
            }

            w.WritePropertyName(property.Name);
            WriteValue(w, property.Value, values, exe);
        }
    }

    private static void WriteValue(Utf8JsonWriter w, JsonElement value, Dictionary<string, string> values, string exe)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                var s = value.GetString() ?? string.Empty;
                w.WriteStringValue(s == "{exe}" ? exe : Expand(s, values));
                break;
            case JsonValueKind.Object:
                w.WriteStartObject();
                foreach (var property in value.EnumerateObject())
                {
                    w.WritePropertyName(property.Name);
                    WriteValue(w, property.Value, values, exe);
                }

                w.WriteEndObject();
                break;
            case JsonValueKind.Array:
                w.WriteStartArray();
                foreach (var element in value.EnumerateArray())
                {
                    WriteValue(w, element, values, exe);
                }

                w.WriteEndArray();
                break;
            default:
                value.WriteTo(w);
                break;
        }
    }

    private static string TomlSnippet(IReadOnlyList<string> segments, JsonElement value, Dictionary<string, string> values, string exe, IReadOnlyList<string> leading)
    {
        var text = new StringBuilder("[").Append(string.Join('.', segments)).Append(']').Append('\n');
        var exeCommand = value.TryGetProperty("command", out var c) && c.GetString() == "{exe}";
        foreach (var property in value.EnumerateObject())
        {
            text.Append(property.Name).Append(" = ");
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var items = (property.Name == "args" && exeCommand ? leading : []).Concat(property.Value.EnumerateArray().Select(e => Expand(e.GetString() ?? string.Empty, values)));
                text.Append('[').Append(string.Join(", ", items.Select(TomlString))).Append(']');
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                var s = property.Value.GetString() ?? string.Empty;
                text.Append(TomlString(s == "{exe}" ? exe : Expand(s, values)));
            }
            else
            {
                text.Append(property.Value.GetRawText());
            }

            text.Append('\n');
        }

        return text.ToString().TrimEnd('\n');
    }

    private static string TomlString(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string? ResolvePath(JsonElement client, string name, string platform, Dictionary<string, string> values)
    {
        if (!client.TryGetProperty(name, out var path))
        {
            return null;
        }

        var text = path.ValueKind switch
        {
            JsonValueKind.String => path.GetString(),
            JsonValueKind.Object => String(path, platform) ?? String(path, "default"),
            _ => null,
        };
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var expanded = Expand(text, values);
        try
        {
            return Path.IsPathRooted(expanded) ? Path.GetFullPath(expanded) : expanded;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return expanded;
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Expand(string text, Dictionary<string, string> values) =>
        Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);

    private static string Indent(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.Length == 0 ? l : "  " + l));

    [GeneratedRegex(@"\{([a-z][a-z_]*)\}")]
    private static partial Regex Placeholder();
}
