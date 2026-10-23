using Cutlass.App.Services.Smoke;

namespace Cutlass.App.Services;

/// <summary>
/// The command line: <c>cutlass &lt;project.cutlass&gt;</c> opens a project and <c>--import &lt;file&gt;...</c> imports
/// files at startup (into an untitled project when none is opened), placing them end to end like a drop on the
/// welcome overlay. <c>--smoke-script &lt;file&gt;</c> runs a developer smoke script after startup and exits with its
/// result (see <see cref="SmokeScript"/>); <c>--smoke-log &lt;file&gt;</c> chooses where its log goes.
/// <c>--agent-server [--port N]</c> turns agent connections on for this run without saving it (AGENT_INTEGRATION 6.4);
/// <c>--port</c> without <c>--agent-server</c> is ignored with a log line.
/// </summary>
/// <param name="ProjectPath">The project to open, or null.</param>
/// <param name="ImportPaths">Files or folders to import.</param>
/// <param name="Unknown">Arguments that were not understood.</param>
public sealed record CommandLineOptions(string? ProjectPath, IReadOnlyList<string> ImportPaths, IReadOnlyList<string> Unknown)
{
    /// <summary>No arguments.</summary>
    public static CommandLineOptions Empty { get; } = new(null, [], []);

    /// <summary>The smoke script to run after startup, or null for a normal session.</summary>
    public string? SmokeScriptPath { get; init; }

    /// <summary>Where the smoke log is written, or null for <c>&lt;script&gt;.log</c>.</summary>
    public string? SmokeLogPath { get; init; }

    /// <summary>True with <c>--agent-server</c>: the agent HTTP API runs for this session whatever the setting says.</summary>
    public bool AgentServer { get; init; }

    /// <summary>The port of <c>--port N</c> (1024 to 65535), or null. Used only with <see cref="AgentServer"/>.</summary>
    public int? AgentPort { get; init; }

    /// <summary>True when <c>--port</c> was given without <c>--agent-server</c> (it is ignored; the app logs a line).</summary>
    public bool PortIgnored => AgentPort is not null && !AgentServer;

    /// <summary>
    /// Parses the arguments. <c>--smoke-script</c> and <c>--smoke-log</c> take the next argument wherever they
    /// appear; every other argument after <c>--import</c> is a path to import; the first other argument ending in
    /// <c>.cutlass</c> is the project.
    /// </summary>
    /// <param name="args">The arguments (without the executable).</param>
    /// <returns>The options.</returns>
    public static CommandLineOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return Empty;
        }

        string? project = null;
        string? smokeScript = null;
        string? smokeLog = null;
        var imports = new List<string>();
        var unknown = new List<string>();
        var importing = false;
        var agentServer = false;
        int? port = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (arg is "--import" or "-i")
            {
                importing = true;
                continue;
            }

            if (arg == "--agent-server")
            {
                agentServer = true;
                continue;
            }

            if (arg == "--port")
            {
                if (i + 1 < args.Count && int.TryParse(args[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) && n is >= 1024 and <= 65535)
                {
                    i++;
                    port = n;
                }
                else
                {
                    unknown.Add(arg);
                }

                continue;
            }

            if (arg is "--smoke-script" or "--smoke-log")
            {
                if (i + 1 < args.Count && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    i++;
                    if (arg == "--smoke-script")
                    {
                        smokeScript = args[i];
                    }
                    else
                    {
                        smokeLog = args[i];
                    }
                }
                else
                {
                    unknown.Add(arg);
                }

                continue;
            }

            if (importing)
            {
                imports.Add(arg);
            }
            else if (project is null && arg.EndsWith(".cutlass", StringComparison.OrdinalIgnoreCase))
            {
                project = arg;
            }
            else
            {
                unknown.Add(arg);
            }
        }

        if (project is null && imports.Count == 0 && unknown.Count == 0 && smokeScript is null && smokeLog is null && !agentServer && port is null)
        {
            return Empty;
        }

        return new CommandLineOptions(project, imports, unknown)
        {
            SmokeScriptPath = smokeScript,
            SmokeLogPath = smokeLog,
            AgentServer = agentServer,
            AgentPort = port,
        };
    }
}
