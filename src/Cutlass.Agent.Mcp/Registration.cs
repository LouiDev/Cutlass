using System.Text;
using System.Text.Json;

namespace Cutlass.Agent.Mcp;

/// <summary>
/// The registration text of <c>--print-registration</c> (docs/AGENT_INTEGRATION.md 5.5): the <c>claude mcp add</c>
/// commands and the Claude Desktop JSON, filled in with the path of the running exe. Run through <c>dotnet</c> (a
/// framework-dependent build), the command is <c>dotnet</c> with the dll as its first argument.
/// </summary>
public static class Registration
{
    /// <summary>The server name registered with the clients.</summary>
    public const string ServerName = "cutlass";

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

    /// <summary>Builds the registration text.</summary>
    /// <param name="command">The command.</param>
    /// <param name="args">The leading arguments (the dll when run through <c>dotnet</c>).</param>
    /// <param name="windows">True to quote for Windows shells.</param>
    /// <returns>The text, ending with a newline.</returns>
    public static string Build(string command, IReadOnlyList<string> args, bool windows)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);
        var line = string.Join(' ', new[] { command }.Concat(args).Select(a => Quote(a, windows)));
        var project = windows ? @"D:\Edits\project.cutlass" : "/home/me/edits/project.cutlass";
        var footage = windows ? @"D:\Footage" : "/home/me/footage";
        var renders = windows ? @"D:\Renders" : "/home/me/renders";

        var text = new StringBuilder();
        text.AppendLine("Claude Code (add --scope user to register it for every project):");
        text.AppendLine($"  claude mcp add {ServerName} -- {line}");
        text.AppendLine();
        text.AppendLine("Claude Code, headless (no app; the engine runs in the MCP server on one project file):");
        text.AppendLine($"  claude mcp add {ServerName}-headless -- {line} --headless --project {Quote(project, windows)} --allow-read {Quote(footage, windows)} --allow-write {Quote(renders, windows)}");
        text.AppendLine();
        text.AppendLine("Claude Desktop (claude_desktop_config.json); Cursor and other clients take the same command and args:");
        text.AppendLine(DesktopJson(command, args));
        text.AppendLine();
        text.AppendLine("The default mode proxies to the running Cutlass app: turn on Settings > Agents > Allow agent connections,");
        text.AppendLine("or start Cutlass.App with --agent-server.");
        return text.ToString();
    }

    /// <summary>The Claude Desktop configuration for this exe.</summary>
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
}
