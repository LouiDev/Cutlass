using System.Globalization;

namespace Cutlass.Agent.Mcp;

/// <summary>
/// The parsed command line of <c>Cutlass.Agent.Mcp</c> (docs/AGENT_INTEGRATION.md 5.1). Parsing never throws: a bad
/// command line yields <see cref="Error"/>, which <see cref="Program"/> prints with the usage text.
/// </summary>
public sealed class McpOptions
{
    /// <summary>The usage text printed by <c>--help</c> and after a bad command line.</summary>
    public const string Usage =
        """
        Cutlass.Agent.Mcp: the Cutlass MCP server and headless agent host.

        Usage:
          Cutlass.Agent.Mcp [--launch <Cutlass.App path>]
              MCP over stdio, proxying to the running Cutlass app (Settings > Agents > Allow agent connections).
          Cutlass.Agent.Mcp --headless --project <file> [--create] [--allow-read <dir>]... [--allow-write <dir>]...
              MCP over stdio with the editing engine in this process (no app, no window).
          Cutlass.Agent.Mcp serve --project <file> [--create] [--port N] [--allow-read <dir>]... [--allow-write <dir>]...
              No MCP: the headless engine plus the HTTP API for scripts; runs until /v1/shutdown or Ctrl+C.
          Cutlass.Agent.Mcp --print-registration
              Prints how to register this exe with every agent client profile (CLI command, config entry or harness text).
          Cutlass.Agent.Mcp --help
        """;

    /// <summary>The mode.</summary>
    public McpMode Mode { get; private init; } = McpMode.Proxy;

    /// <summary>The full path of the project file for <c>--headless</c> and <c>serve</c>.</summary>
    public string? ProjectPath { get; private init; }

    /// <summary>True to create the project file when it is missing (<c>--create</c>).</summary>
    public bool Create { get; private init; }

    /// <summary>The fixed HTTP port for <c>serve</c>, or null for the default with fallback.</summary>
    public int? Port { get; private init; }

    /// <summary>Extra folders the agent may read from (<c>--allow-read</c>, repeatable), as full paths.</summary>
    public IReadOnlyList<string> AllowRead { get; private init; } = [];

    /// <summary>Extra folders the agent may write to (<c>--allow-write</c>, repeatable), as full paths.</summary>
    public IReadOnlyList<string> AllowWrite { get; private init; } = [];

    /// <summary>The app to start with <c>--agent-server</c> when none serves agents (<c>--launch</c>, proxy mode only).</summary>
    public string? LaunchPath { get; private init; }

    /// <summary>A message describing what is wrong with the command line, or null when it parsed.</summary>
    public string? Error { get; private init; }

    /// <summary>Parses the command line.</summary>
    /// <param name="args">The arguments without the exe name.</param>
    /// <returns>The options; check <see cref="Error"/>.</returns>
    public static McpOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var serve = false;
        var headless = false;
        var printRegistration = false;
        var help = false;
        var create = false;
        string? project = null;
        string? launch = null;
        int? port = null;
        var allowRead = new List<string>();
        var allowWrite = new List<string>();

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "serve" when i == 0:
                    serve = true;
                    break;
                case "--headless":
                    headless = true;
                    break;
                case "--create":
                    create = true;
                    break;
                case "--print-registration":
                    printRegistration = true;
                    break;
                case "--help" or "-h" or "-?" or "/?":
                    help = true;
                    break;
                case "--project" or "--allow-read" or "--allow-write" or "--port" or "--launch":
                    if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        return Fail($"{arg} needs a value.");
                    }

                    var value = args[++i];
                    switch (arg)
                    {
                        case "--project":
                            project = value;
                            break;
                        case "--allow-read":
                            allowRead.Add(value);
                            break;
                        case "--allow-write":
                            allowWrite.Add(value);
                            break;
                        case "--launch":
                            launch = value;
                            break;
                        default:
                            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p < 1024 || p > 65535)
                            {
                                return Fail($"--port must be a number from 1024 to 65535, not '{value}'.");
                            }

                            port = p;
                            break;
                    }

                    break;
                default:
                    return Fail($"Unknown argument '{arg}'.");
            }
        }

        if (help)
        {
            return new McpOptions { Mode = McpMode.Help };
        }

        if (printRegistration)
        {
            return new McpOptions { Mode = McpMode.PrintRegistration };
        }

        if (serve && headless)
        {
            return Fail("Use either serve or --headless, not both.");
        }

        var mode = serve ? McpMode.Serve : headless ? McpMode.Headless : McpMode.Proxy;
        if (mode == McpMode.Proxy)
        {
            if (project is not null || create || allowRead.Count > 0 || allowWrite.Count > 0 || port is not null)
            {
                return Fail("--project, --create, --port, --allow-read and --allow-write need --headless or serve; the proxy uses the running app's settings.");
            }
        }
        else
        {
            if (launch is not null)
            {
                return Fail("--launch works only in proxy mode (no --headless, no serve).");
            }

            if (project is null)
            {
                return Fail(mode == McpMode.Serve ? "serve needs --project <file>." : "--headless needs --project <file>.");
            }

            if (port is not null && mode != McpMode.Serve)
            {
                return Fail("--port works only with serve.");
            }
        }

        return new McpOptions
        {
            Mode = mode,
            ProjectPath = project is null ? null : Path.GetFullPath(project),
            Create = create,
            Port = port,
            AllowRead = [.. allowRead.Select(Path.GetFullPath)],
            AllowWrite = [.. allowWrite.Select(Path.GetFullPath)],
            LaunchPath = launch is null ? null : Path.GetFullPath(launch),
        };
    }

    private static McpOptions Fail(string message) => new() { Mode = McpMode.Help, Error = message };
}
