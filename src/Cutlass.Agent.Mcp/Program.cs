using System.Net;
using Cutlass.Agent.Hosting;
using Cutlass.Agent.Http;
using Cutlass.Agent.Mcp.Backend;
using Cutlass.Agent.Mcp.Prompts;
using Cutlass.Agent.Mcp.Resources;
using Cutlass.Agent.Mcp.Tools;
using Cutlass.Agent.Tools.Audit;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.Media.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Cutlass.Agent.Mcp;

/// <summary>
/// The entry point of <c>Cutlass.Agent.Mcp</c> (docs/AGENT_INTEGRATION.md 5.1). In the MCP modes stdout is the
/// protocol, so every log line goes to stderr and nothing else writes to stdout. Exit codes: 0 success, 1 a startup
/// failure (named on stderr), 2 a bad command line.
/// </summary>
public static class Program
{
    /// <summary>The MCP server name reported at initialisation.</summary>
    public const string ServerName = "cutlass";

    /// <summary>Runs the exe.</summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        var options = McpOptions.Parse(args);
        if (options.Error is { } error)
        {
            await Console.Error.WriteLineAsync(error + Environment.NewLine + Environment.NewLine + McpOptions.Usage).ConfigureAwait(false);
            return 2;
        }

        switch (options.Mode)
        {
            case McpMode.Help:
                Console.Out.Write(McpOptions.Usage + Environment.NewLine);
                return 0;
            case McpMode.PrintRegistration:
                Console.Out.Write(await PrintRegistrationAsync().ConfigureAwait(false));
                return 0;
        }

        using var loggerFactory = CreateLoggerFactory();
        var log = loggerFactory.CreateLogger("Cutlass.Agent.Mcp");
        try
        {
            return options.Mode switch
            {
                McpMode.Proxy => await RunMcpAsync(
                    new HttpBackend(EndpointLocator.ForCurrentUser(), options.LaunchPath, loggerFactory.CreateLogger<HttpBackend>()),
                    loggerFactory).ConfigureAwait(false),
                McpMode.Headless => await RunHeadlessAsync(options, loggerFactory, log).ConfigureAwait(false),
                _ => await RunServeAsync(options, loggerFactory, log).ConfigureAwait(false),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or HttpListenerException)
        {
            log.LogError("{Message}", ex.Message);
            return 1;
        }
    }

    // docs/AGENT_INTEGRATION.md 9.4: every client profile's registration, from the profile file the app ships beside
    // the exes (or the checkout's copy); without it the generic entry.
    private static async Task<string> PrintRegistrationAsync()
    {
        var (command, leading) = Registration.CurrentCommand(Environment.ProcessPath, AppContext.BaseDirectory);
        var windows = OperatingSystem.IsWindows();
        if (Registration.FindProfileFile(AppContext.BaseDirectory) is { } file)
        {
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var appData = windows
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(home, ".config");
                var platform = windows ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
                var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                return Registration.BuildFromProfiles(json, command, leading, windows, home, appData, platform);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                await Console.Error.WriteLineAsync("The client profiles " + file + " could not be read: " + ex.Message).ConfigureAwait(false);
            }
        }

        return Registration.Build(command, leading, windows);
    }

    private static ILoggerFactory CreateLoggerFactory() =>
        LoggerFactory.Create(builder => ConfigureLogging(builder));

    private static void ConfigureLogging(ILoggingBuilder builder)
    {
        builder.ClearProviders();

        // stdout is the MCP protocol: every level goes to stderr.
        builder.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.SetMinimumLevel(LogLevel.Information);
        builder.AddFilter("Microsoft", LogLevel.Warning);
        builder.AddFilter("ModelContextProtocol", LogLevel.Warning);
    }

    private static async Task<int> RunHeadlessAsync(McpOptions options, ILoggerFactory loggerFactory, ILogger log)
    {
        if (RefuseProjectOfLiveApp(options.ProjectPath!, log))
        {
            return 1;
        }

        var host = await HeadlessHost.StartAsync(HostOptions(options)).ConfigureAwait(false);
        LogEngineState(host, log);
        return await RunMcpAsync(new InProcessBackend(host, ownsHost: true, loggerFactory.CreateLogger<InProcessBackend>()), loggerFactory).ConfigureAwait(false);
    }

    /// <summary>Serves MCP over stdio until stdin closes, then releases a held lock and disposes the backend.</summary>
    private static async Task<int> RunMcpAsync(IAgentBackend backend, ILoggerFactory loggerFactory)
    {
        await using (backend.ConfigureAwait(false))
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                // The command line is ours (McpOptions), not configuration.
                Args = [],
                ApplicationName = "Cutlass.Agent.Mcp",
                ContentRootPath = AppContext.BaseDirectory,
            });
            ConfigureLogging(builder.Logging);
            builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);
            builder.Services.AddSingleton(backend);

            var tools = new CutlassTools(backend);
            builder.Services
                .AddMcpServer(o =>
                {
                    o.ServerInfo = new Implementation
                    {
                        Name = ServerName,
                        Title = "Cutlass video editor",
                        Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
                    };
                    o.ServerInstructions = ServerInstructions.Text;
                })
                .WithStdioServerTransport()
                .WithTools(tools.CreateTools())
                .WithResources<CutlassResources>()
                .WithPrompts<CutlassPrompts>();

            loggerFactory.CreateLogger("Cutlass.Agent.Mcp").LogInformation("Serving MCP over stdio ({Kind}).", backend.Kind);
            using var app = builder.Build();
            await app.RunAsync().ConfigureAwait(false);
        }

        return 0;
    }

    private static async Task<int> RunServeAsync(McpOptions options, ILoggerFactory loggerFactory, ILogger log)
    {
        if (RefuseProjectOfLiveApp(options.ProjectPath!, log))
        {
            return 1;
        }

        var paths = AppPaths.ForCurrentPlatform();
        var endpointFile = new EndpointFile(paths.AgentDirectory);
        if (endpointFile.ReadLiveOther() is { } other)
        {
            // AGENT_INTEGRATION 4.1: one server per user.
            log.LogError(
                "Another Cutlass process (pid {Pid}, {Kind}) already serves agents at {BaseUrl}. Stop it first, or use it.",
                other.Pid,
                other.HostKind,
                other.BaseUrl);
            return 1;
        }

        await using var host = await HeadlessHost.StartAsync(HostOptions(options)).ConfigureAwait(false);
        LogEngineState(host, log);
        using var dispatcher = new ToolDispatcher(host, new AuditLog(host.Engine.Log));
        var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.ShutdownHandler = save =>
        {
            stop.TrySetResult(save);
            return Task.CompletedTask;
        };

        var tokens = new TokenStore(paths.AgentDirectory);
        await using var server = new AgentHttpServer(dispatcher, tokens.LoadOrCreate());
        var port = options.Port ?? host.Engine.Settings.AgentPort;
        await server.StartAsync(port, fixedPort: options.Port is not null).ConfigureAwait(false);
        endpointFile.Write(EndpointFile.ForThisProcess(server.BaseUrl!, host.Kind, tokens.TokenFile, host.ProjectPath));

        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            stop.TrySetResult(false);
        }

        Console.CancelKeyPress += OnCancel;
        try
        {
            // Not MCP: scripts read these two lines from stdout.
            Console.Out.WriteLine("baseUrl: " + server.BaseUrl);
            Console.Out.WriteLine("tokenFile: " + tokens.TokenFile);
            Console.Out.Flush();
            log.LogInformation("Serving the Cutlass agent API until POST /v1/shutdown or Ctrl+C.");

            var save = await stop.Task.ConfigureAwait(false);
            if (save)
            {
                await SaveAsync(host, log).ConfigureAwait(false);
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            await server.StopAsync().ConfigureAwait(false);
            endpointFile.DeleteIfOwned();
        }

        return 0;
    }

    /// <summary>Saves the project to its own path for <c>/v1/shutdown {save: true}</c>.</summary>
    private static async Task SaveAsync(HeadlessHost host, ILogger log)
    {
        var (project, path) = await host.Dispatcher.InvokeAsync(() => (host.Session.Current, host.ProjectPath), CancellationToken.None).ConfigureAwait(false);
        if (path is null)
        {
            log.LogWarning("Shutdown asked to save, but the project has no file; an autosave is written instead.");
            return;
        }

        await host.Engine.Projects.SaveAsync(project, path, CancellationToken.None).ConfigureAwait(false);
        await host.Dispatcher.InvokeAsync(
            () =>
            {
                if (ReferenceEquals(host.Session.Current, project))
                {
                    host.Session.MarkSaved();
                }
            },
            CancellationToken.None).ConfigureAwait(false);
        log.LogInformation("Saved {Path}.", path);
    }

    private static HeadlessHostOptions HostOptions(McpOptions options) => new()
    {
        ProjectPath = options.ProjectPath,
        Create = options.Create,
        ReadFolders = options.AllowRead,
        WriteFolders = options.AllowWrite,
    };

    /// <summary>
    /// AGENT_INTEGRATION 8 "Two writers on one file": refuse a project that a live Cutlass process (the app or another
    /// headless host) names in endpoint.json. Checked by <c>--headless</c> and <c>serve</c> before the engine starts.
    /// </summary>
    private static bool RefuseProjectOfLiveApp(string projectPath, ILogger log)
    {
        var paths = AppPaths.ForCurrentPlatform();
        if (ProjectOwnerGuard.Check(new EndpointFile(paths.AgentDirectory), paths, projectPath) is { } refusal)
        {
            log.LogError("{Message}", refusal);
            return true;
        }

        return false;
    }

    private static void LogEngineState(HeadlessHost host, ILogger log)
    {
        if (host.Services.IsMediaReady)
        {
            log.LogInformation("Headless engine ready; project {Path}.", host.ProjectPath ?? "(untitled)");
        }
        else
        {
            log.LogWarning("ffmpeg is not ready: {Reason}. Reads work; media tools report unavailable.", host.Services.NotReadyReason);
        }
    }
}
