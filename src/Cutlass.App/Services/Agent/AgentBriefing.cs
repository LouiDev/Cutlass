using System.Globalization;
using System.Text.RegularExpressions;
using Cutlass.Agent.Api;
using Cutlass.Agent.Lock;
using Cutlass.App.Services.Agent.Clients.Profiles;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.Services.Agent;

/// <summary>
/// "Copy briefing for your agent" (MCP_HOSTING_CONCEPT 5, AGENT_INTEGRATION 6.6 and 9.4): a pasteable English prompt
/// with one vendor-neutral body (what is open and the steps) and a per-target tail chosen by the client profile: the
/// MCP tool names and that client's registration as the fallback, the HTTP contract for a tool-calling harness, the
/// <c>curl</c> examples for plain HTTP, or a generic tail when no client is known. The defaults are embedded resources
/// (<c>Briefing/briefing.md</c> and <c>Briefing/briefing-tail-&lt;kind&gt;.md</c>); a user copy at
/// <c>&lt;data&gt;/agent/briefing.md</c> (or the file named by the <c>AgentBriefingTemplatePath</c> setting) replaces the
/// body, and <c>&lt;data&gt;/agent/briefing-tail-&lt;profile id&gt;.md</c> replaces one target's tail. Placeholders are
/// <c>{name}</c> words from <see cref="Placeholders"/>; unknown words stay as they are. Rendering is pure; loading reads
/// files off the calling thread.
/// </summary>
public static partial class AgentBriefing
{
    /// <summary>The user copy of the body in the agent folder.</summary>
    public const string TemplateFileName = "briefing.md";

    /// <summary>The id of the generic tail (no client known), also its user file's suffix.</summary>
    public const string GenericTarget = "generic";

    /// <summary>The most tracks listed in <c>{tracks}</c> (MCP_HOSTING_CONCEPT 5.1: the first lines).</summary>
    public const int MaxTrackLines = 8;

    /// <summary>The most media names listed in <c>{media}</c>.</summary>
    public const int MaxMediaNames = 12;

    /// <summary>
    /// The placeholders: <c>project</c>, <c>sequence</c>, <c>tracks</c>, <c>media</c>, <c>lock</c>, <c>url</c>,
    /// <c>exe</c>, <c>registration</c> (MCP_HOSTING_CONCEPT 5.1), <c>token</c> and <c>docs</c>, plus the target's
    /// <c>client</c> (its name), <c>tools_url</c> (the tool definitions in its format) and <c>example</c> (a runnable
    /// example script).
    /// </summary>
    public static IReadOnlyList<string> Placeholders { get; } =
        ["project", "sequence", "tracks", "media", "lock", "url", "exe", "registration", "token", "docs", "client", "tools_url", "example"];

    /// <summary>The user file that replaces one target's tail: <c>briefing-tail-&lt;id&gt;.md</c>.</summary>
    /// <param name="targetId">The profile id, or <see cref="GenericTarget"/>.</param>
    /// <returns>The file name.</returns>
    public static string TailFileName(string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        return "briefing-tail-" + targetId + ".md";
    }

    /// <summary>The embedded default body.</summary>
    /// <returns>The body with <c>\n</c> line ends.</returns>
    public static string DefaultBody() => Resource(TemplateFileName);

    /// <summary>The embedded default tail of a kind of target.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The tail with <c>\n</c> line ends.</returns>
    public static string DefaultTail(AgentClientBriefingKind kind) => Resource(kind switch
    {
        AgentClientBriefingKind.Mcp => "briefing-tail-mcp.md",
        AgentClientBriefingKind.Harness => "briefing-tail-harness.md",
        AgentClientBriefingKind.Http => "briefing-tail-http.md",
        _ => "briefing-tail-generic.md",
    });

    /// <summary>A body and a tail joined with one blank line, ending with one line end.</summary>
    /// <param name="body">The body.</param>
    /// <param name="tail">The tail.</param>
    /// <returns>The template.</returns>
    public static string Compose(string body, string tail)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(tail);
        return Normal(body).TrimEnd('\n') + "\n\n" + Normal(tail).TrimEnd('\n') + "\n";
    }

    /// <summary>
    /// Loads the template off the calling thread: the body from the setting's file, the user copy in the agent folder
    /// or the embedded default; the tail from <c>briefing-tail-&lt;target&gt;.md</c> in the agent folder or the
    /// embedded default of its kind. A file that cannot be read falls back to the next source.
    /// </summary>
    /// <param name="agentDirectory">The agent folder (<c>AppPaths.AgentDirectory</c>).</param>
    /// <param name="overridePath">The <c>AgentBriefingTemplatePath</c> setting, or null.</param>
    /// <param name="targetId">The profile id of the target, or <see cref="GenericTarget"/>.</param>
    /// <param name="kind">The kind of tail.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The body, the tail and where they came from.</returns>
    public static Task<AgentBriefingTemplate> LoadTemplateAsync(string agentDirectory, string? overridePath, string targetId, AgentClientBriefingKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(agentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        return Task.Run(
            () =>
            {
                var bodyCandidates = new List<string>();
                if (!string.IsNullOrWhiteSpace(overridePath))
                {
                    bodyCandidates.Add(overridePath);
                }

                bodyCandidates.Add(Path.Combine(agentDirectory, TemplateFileName));
                var (body, bodySource) = FirstReadable(bodyCandidates) ?? (DefaultBody(), "built-in");
                var (tail, tailSource) = FirstReadable([Path.Combine(agentDirectory, TailFileName(targetId))]) ?? (DefaultTail(kind), "built-in");
                return new AgentBriefingTemplate(body, tail, bodySource, tailSource);
            },
            cancellationToken);
    }

    /// <summary>
    /// The live values of the project placeholders (MCP_HOSTING_CONCEPT 5.1) plus the given connection and target
    /// values. Invariant culture.
    /// </summary>
    /// <param name="project">The open project.</param>
    /// <param name="hasProject">False when no project is open (the welcome overlay shows).</param>
    /// <param name="projectPath">The project file, or null when untitled.</param>
    /// <param name="dirty">True with unsaved changes.</param>
    /// <param name="lockState">The edit lock.</param>
    /// <param name="url">The agent server's base URL, for example <c>http://localhost:47390/</c>.</param>
    /// <param name="exe">The MCP exe.</param>
    /// <param name="registration">The target client's registration (a command or a configuration snippet), indented.</param>
    /// <param name="token">The token text: the shell line that sets <c>CUTLASS_TOKEN</c> (plain HTTP) or the token or its file (harness).</param>
    /// <param name="docs">The agent docs path.</param>
    /// <param name="client">The target client's name.</param>
    /// <param name="toolsUrl">The tool definitions URL in the target's format, or null for the Markdown reference.</param>
    /// <param name="example">The target's example script, or null.</param>
    /// <returns>The values by placeholder name.</returns>
    public static IReadOnlyDictionary<string, string> Values(
        Project project,
        bool hasProject,
        string? projectPath,
        bool dirty,
        AgentLockState lockState,
        string url,
        string exe,
        string registration,
        string token,
        string docs,
        string client = "your agent client",
        string? toolsUrl = null,
        string? example = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(lockState);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project"] = ProjectText(hasProject, projectPath, dirty),
            ["sequence"] = SequenceText(project),
            ["tracks"] = hasProject ? ProjectDescriber.Summary(project, MaxTrackLines).Replace("\n", "; ", StringComparison.Ordinal) : "none (no project is open)",
            ["media"] = MediaText(project),
            ["lock"] = LockText(lockState),
            ["url"] = url,
            ["exe"] = exe,
            ["registration"] = registration,
            ["token"] = token,
            ["docs"] = docs,
            ["client"] = client,
            ["tools_url"] = toolsUrl ?? url + "v1/tools?format=markdown",
            ["example"] = example ?? "the examples named in " + docs,
        };
    }

    /// <summary>Indents every line of a text by two spaces (a registration inside the tail).</summary>
    /// <param name="text">The text.</param>
    /// <returns>The indented text.</returns>
    public static string Indent(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Join('\n', Normal(text).Split('\n').Select(l => l.Length == 0 ? l : "  " + l));
    }

    /// <summary>Fills the placeholders of a template: each <c>{name}</c> with a value; other braces stay as they are.</summary>
    /// <param name="template">The template.</param>
    /// <param name="values">The values by placeholder name.</param>
    /// <returns>The briefing with <c>\n</c> line ends.</returns>
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        return PlaceholderPattern().Replace(
            Normal(template),
            m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

    /// <summary>The <c>{name}</c> words a rendered text still holds (the smoke check and the tests expect none).</summary>
    /// <param name="text">The rendered text.</param>
    /// <returns>The unfilled placeholders, in order.</returns>
    public static IReadOnlyList<string> Unfilled(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. PlaceholderPattern().Matches(text).Select(m => m.Value)];
    }

    /// <summary>The <c>{token}</c> line of plain HTTP: the token itself after the user confirmed, else a read of the token file.</summary>
    /// <param name="token">The token, or null to point at the file.</param>
    /// <param name="tokenFile">The token file.</param>
    /// <returns>A bash line that sets <c>CUTLASS_TOKEN</c>.</returns>
    public static string TokenLine(string? token, string tokenFile)
    {
        ArgumentNullException.ThrowIfNull(tokenFile);
        return token is { Length: > 0 }
            ? "export CUTLASS_TOKEN='" + token + "'"
            : "export CUTLASS_TOKEN=\"$(cat '" + tokenFile.Replace('\\', '/') + "')\"   # the token file of Cutlass";
    }

    /// <summary>The <c>{token}</c> text of a harness: the token after the user confirmed, else where to read it.</summary>
    /// <param name="token">The token, or null.</param>
    /// <param name="tokenFile">The token file.</param>
    /// <returns>The text.</returns>
    public static string TokenText(string? token, string tokenFile)
    {
        ArgumentNullException.ThrowIfNull(tokenFile);
        return token is { Length: > 0 } ? token : "read it from " + tokenFile.Replace('\\', '/') + " (the token file of Cutlass)";
    }

    /// <summary>"<c>"name" (path)</c>", with ", unsaved changes" when dirty, or "an untitled project".</summary>
    /// <param name="hasProject">False when no project is open.</param>
    /// <param name="path">The project file, or null.</param>
    /// <param name="dirty">Unsaved changes.</param>
    /// <returns>The text.</returns>
    public static string ProjectText(bool hasProject, string? path, bool dirty)
    {
        if (!hasProject)
        {
            return "none is open (create one with cutlass_new_project)";
        }

        var text = string.IsNullOrEmpty(path) ? "an untitled project" : "\"" + ProjectSummaryMapper.NameOf(path) + "\" (" + path + ")";
        return dirty ? text + ", unsaved changes" : text;
    }

    /// <summary>"<c>1920x1080 at 30 fps, 00:00:12:00 (360 frames)</c>".</summary>
    /// <param name="project">The project.</param>
    /// <returns>The text.</returns>
    public static string SequenceText(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var s = project.Sequence.Settings;
        var duration = TimelineQuery.Duration(project);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{s.Width}x{s.Height} at {s.Rate.DisplayName} fps, {Timecode.Format(duration, s.Rate)} ({duration} frames)");
    }

    /// <summary>The imported media file names, at most <see cref="MaxMediaNames"/>, or "none imported yet".</summary>
    /// <param name="project">The project.</param>
    /// <returns>The text.</returns>
    public static string MediaText(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var names = project.Media.Select(m => Path.GetFileName(m.Path.Replace('\\', '/'))).ToList();
        if (names.Count == 0)
        {
            return "none imported yet";
        }

        var shown = string.Join(", ", names.Take(MaxMediaNames));
        return names.Count <= MaxMediaNames
            ? shown
            : shown + string.Create(CultureInfo.InvariantCulture, $" and {names.Count - MaxMediaNames} more");
    }

    /// <summary>"free", "held by X", "paused, held by X" or "free, X is waiting".</summary>
    /// <param name="state">The lock state.</param>
    /// <returns>The text.</returns>
    public static string LockText(AgentLockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var name = string.IsNullOrWhiteSpace(state.AgentName) ? "an agent" : state.AgentName;
        return state.Phase switch
        {
            LockPhase.Held => "held by " + name,
            LockPhase.Paused => "paused, held by " + name,
            LockPhase.Pending => "free, " + (string.IsNullOrWhiteSpace(state.PendingAgent) ? "an agent" : state.PendingAgent) + " is waiting",
            _ => "free",
        };
    }

    private static string Normal(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Resource(string fileName)
    {
        var name = "Cutlass.App.Services.Agent.Briefing." + fileName;
        using var stream = typeof(AgentBriefing).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("The embedded briefing template " + name + " is missing.");
        using var reader = new StreamReader(stream);
        return Normal(reader.ReadToEnd());
    }

    private static (string Text, string Source)? FirstReadable(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path) && File.ReadAllText(path) is { } text && !string.IsNullOrWhiteSpace(text))
                {
                    return (Normal(text), path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Unreadable: the next source.
            }
        }

        return null;
    }

    [GeneratedRegex(@"\{([a-z][a-z_]*)\}")]
    private static partial Regex PlaceholderPattern();
}
