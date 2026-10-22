using System.Globalization;

namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// Reads what a client's command line tool says about the <c>cutlass</c> server (AGENT_INTEGRATION 9.3), driven only
/// by the profile's <see cref="AgentClientProbeSpec"/>. The parse is tolerant: a missing tool is not installed; any
/// "not registered" text means not registered; the command line (or the server's line in a list) is compared with this
/// app's MCP exe (current), another Cutlass MCP exe (stale) or anything else (foreign); without such a line the text
/// is searched for the exe. A failure without a usable line is an error with the tool's first line.
/// </summary>
public static class AgentClientCliParser
{
    /// <summary>The file name every Cutlass MCP exe starts with: an entry naming it was written for Cutlass.</summary>
    public const string McpExeName = "Cutlass.Agent.Mcp";

    /// <summary>The MCP server's name in every client.</summary>
    public const string ServerName = "cutlass";

    /// <summary>Parses one run of the check.</summary>
    /// <param name="result">The tool's result.</param>
    /// <param name="spec">The profile's check.</param>
    /// <param name="expectedExe">This app's MCP exe.</param>
    /// <param name="timeout">The timeout the run had (for the message).</param>
    /// <returns>The probe (installed unless the tool was missing).</returns>
    public static AgentClientProbe Parse(CliResult result, AgentClientProbeSpec spec, string expectedExe, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(expectedExe);
        if (result.NotFound)
        {
            return new AgentClientProbe(false, null, AgentClientState.NotInstalled, result.StandardError.Trim());
        }

        if (result.TimedOut)
        {
            return new AgentClientProbe(true, null, AgentClientState.Error, "The " + spec.Command + " command did not answer within " + ((int)timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s.");
        }

        var text = (result.StandardOutput + "\n" + result.StandardError).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (spec.NotRegisteredWhen?.Any(t => t.Length > 0 && text.Contains(t, StringComparison.OrdinalIgnoreCase)) == true)
        {
            return new AgentClientProbe(true, null, AgentClientState.NotRegistered);
        }

        string? scope = null;
        string? command = null;
        var listed = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (spec.ScopeField is { Length: > 0 } scopeField && line.StartsWith(scopeField, StringComparison.OrdinalIgnoreCase))
            {
                scope = ScopeOf(line[scopeField.Length..]);
            }
            else if (spec.CommandField is { Length: > 0 } commandField && line.StartsWith(commandField, StringComparison.OrdinalIgnoreCase))
            {
                command = Unquote(line[commandField.Length..].Trim());
            }
            else if (spec.ListPrefix is { Length: > 0 } prefix && line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && line.Length > prefix.Length)
            {
                // A list line: "cutlass: <command> [args] - <health>".
                var rest = line[prefix.Length..].Trim();
                var health = rest.LastIndexOf(" - ", StringComparison.Ordinal);
                command ??= Unquote((health > 0 ? rest[..health] : rest).Trim());
                listed = true;
            }
        }

        if (result.ExitCode != 0 && command is null)
        {
            return new AgentClientProbe(true, null, AgentClientState.Error, FirstLine(result.CombinedOutput, spec.Command));
        }

        if (command is { Length: > 0 })
        {
            // A list line holds the command and its arguments; the exe is its start.
            var same = SamePath(command, expectedExe) ||
                (listed && command.StartsWith(expectedExe, PathComparison) && (command.Length == expectedExe.Length || command[expectedExe.Length] == ' '));
            var state = same ? AgentClientState.Current
                : command.Contains(McpExeName, StringComparison.OrdinalIgnoreCase) ? AgentClientState.Stale
                : AgentClientState.Foreign;
            return new AgentClientProbe(true, null, state, command, scope);
        }

        if (text.Contains(expectedExe, PathComparison))
        {
            return new AgentClientProbe(true, null, AgentClientState.Current, expectedExe, scope);
        }

        if (text.Contains(McpExeName, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentClientProbe(true, null, AgentClientState.Stale, FirstLine(text, spec.Command), scope);
        }

        return text.Contains(ServerName, StringComparison.OrdinalIgnoreCase) && !listed && result.ExitCode == 0 && spec.ListPrefix is null
            ? new AgentClientProbe(true, null, AgentClientState.Foreign, FirstLine(text, spec.Command), scope)
            : new AgentClientProbe(true, null, AgentClientState.NotRegistered);
    }

    /// <summary>True when two command texts name the same file (full path, either slash, case per platform).</summary>
    /// <param name="a">One path.</param>
    /// <param name="b">The other path.</param>
    /// <returns>True when they are the same.</returns>
    public static bool SamePath(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return string.Equals(Normal(a), Normal(b), PathComparison);

        static string Normal(string p)
        {
            var t = Unquote(p.Trim());
            if (t.Length > 0 && (Path.IsPathRooted(t) || t.Contains('/', StringComparison.Ordinal) || t.Contains('\\', StringComparison.Ordinal)))
            {
                try
                {
                    t = Path.GetFullPath(t);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    // Not a path: compare the text.
                }
            }

            return t.Replace('\\', '/').TrimEnd('/');
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string? ScopeOf(string text)
    {
        var t = text.Trim();
        return t.Length == 0 ? null : t.Split(' ', 2)[0].ToLowerInvariant();
    }

    private static string Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' ? text[1..^1] : text;

    private static string FirstLine(string text, string command)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return string.IsNullOrEmpty(line) ? "The " + command + " command failed without a message." : line;
    }
}
