using System.Globalization;
using Avalonia.Threading;
using Cutlass.Agent.Http;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools.Audit;
using Cutlass.Agent.Tools.Dispatch;
using Cutlass.App.ViewModels;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Media.Platform;
using Cutlass.Media.Settings;

namespace Cutlass.App.Services.Agent;

/// <summary>
/// Runs the App's agent HTTP server (AGENT_INTEGRATION 2.4, 4.1 and 8): starts and stops it per "Allow agent
/// connections" or <c>--agent-server [--port N]</c>, owns the token and <c>endpoint.json</c>, records calls for the
/// Agent panel, and ticks the <see cref="AgentLockDriver"/> from a 1 s <see cref="DispatcherTimer"/>. A second
/// Cutlass that finds a live owner in <c>endpoint.json</c> does not serve. On Windows the prefix is
/// <c>http://localhost:N/</c>; when http.sys refuses it the controller retries <c>127.0.0.1</c>, then shows the error
/// with the <c>netsh http add urlacl</c> hint. Agent > Start/Stop MCP server (AGENT_INTEGRATION 6.6) switches it for
/// this session on top of the setting (<see cref="StartForSessionAsync"/>, <see cref="StopForSessionAsync"/>); a change
/// of "Allow agent connections" in Settings ends that session switch. UI thread only, except the token and file I/O,
/// which run off it.
/// </summary>
public sealed partial class AgentServerController : IAgentSettingsHost, IAgentServerSwitch, IAsyncDisposable
{
    /// <summary>How many ports after the default are tried when the default is busy.</summary>
    public const int PortProbeCount = 9;

    private readonly AppServices _services;
    private readonly AppAgentHost _host;
    private readonly CommandLineOptions _options;
    private readonly AppLog? _log;
    private readonly TokenStore _tokens;
    private readonly EndpointFile _endpoint;
    private readonly AuditLog _audit;
    private readonly AgentLockDriver _driver;
    private readonly DispatcherTimer _timer;
    private readonly SemaphoreSlim _switch = new(1, 1);
    private ToolDispatcher? _dispatcher;
    private string? _token;
    private string? _baseUrl;
    private bool _disposed;

    // Agent > Start/Stop MCP server: true or false overrides the setting for this session; null follows it.
    private bool? _sessionSwitch;

    // True while StartForSessionAsync saves "Allow agent connections" on, so the change does not end the session switch.
    private bool _savingKeepOn;

    /// <summary>Creates the controller (UI thread). Nothing listens until <see cref="StartAsync"/>.</summary>
    /// <param name="services">The composition root.</param>
    /// <param name="host">The App's agent host.</param>
    /// <param name="options">The command line (<c>--agent-server</c>, <c>--port</c>).</param>
    /// <param name="log">The app log, or null.</param>
    public AgentServerController(AppServices services, AppAgentHost host, CommandLineOptions options, AppLog? log)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(options);
        _services = services;
        _host = host;
        _options = options;
        _log = log;
        _tokens = new TokenStore(services.Paths.AgentDirectory);
        _endpoint = new EndpointFile(services.Paths.AgentDirectory);
        _audit = new AuditLog(log);
        _audit.Recorded += entry => Avalonia.Threading.Dispatcher.UIThread.Post(() => MainWindow.Agent.AddCall(entry));
        _driver = new AgentLockDriver(host);
        _timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
        services.SettingsChanged += OnSettingsChanged;
        MainWindow.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.ProjectPath))
            {
                _ = WriteEndpointAsync();
            }
        };
        StatusText = "Off";
        if (options.PortIgnored)
        {
            log?.Write("agent: --port is ignored without --agent-server");
        }
    }

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public AgentServerStatus Status { get; private set; } = AgentServerStatus.Off;

    /// <inheritdoc />
    public string StatusText { get; private set; }

    /// <inheritdoc />
    public bool IsForcedOn => _options.AgentServer;

    /// <summary>The base URL while listening, for example <c>http://localhost:47390/</c>, else null.</summary>
    public string? BaseUrl => _baseUrl;

    /// <inheritdoc />
    public bool IsSwitchedOn => ShouldServe(_services.Settings);

    /// <inheritdoc />
    public string ExpectedBaseUrl => _baseUrl ?? string.Create(
        CultureInfo.InvariantCulture,
        $"http://{(OperatingSystem.IsWindows() ? "localhost" : "127.0.0.1")}:{(_options.AgentServer && _options.AgentPort is { } port ? port : _services.Settings.AgentPort)}/");

    /// <inheritdoc />
    public string TokenFile => _tokens.TokenFile;

    /// <summary>The audit log of agent calls (the Agent panel reads it).</summary>
    public AuditLog Audit => _audit;

    /// <summary>The tool dispatcher while the server runs, else null.</summary>
    public ToolDispatcher? Tools => _dispatcher;

    /// <summary>The lock driver (its clock is the lock's clock).</summary>
    public AgentLockDriver Driver => _driver;

    /// <inheritdoc />
    public string McpExecutablePath => FindBeside("Cutlass.Agent.Mcp" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty), Path.Combine("src", "Cutlass.Agent.Mcp", "bin", "Debug", "net10.0"));

    /// <inheritdoc />
    public string AgentDocsPath => FindBeside("AGENT_INTEGRATION.md", "docs");

    private MainWindowViewModel MainWindow => _host.MainWindow;

    /// <summary>
    /// True when connections should be on: Agent > Start/Stop MCP server for this session when used, else the
    /// setting or <c>--agent-server</c>.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <returns>True when the server should run.</returns>
    public bool ShouldServe(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return _sessionSwitch ?? (_options.AgentServer || settings.AgentConnectionsEnabled);
    }

    /// <inheritdoc />
    public async Task StartForSessionAsync(bool keepOn)
    {
        _sessionSwitch = true;
        if (keepOn && !_services.Settings.AgentConnectionsEnabled)
        {
            _savingKeepOn = true;
            try
            {
                await _services.ApplySettingsAsync(_services.Settings with { AgentConnectionsEnabled = true }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log?.Write("agent: \"Keep it on\" could not be saved: " + ex.Message);
            }
            finally
            {
                _savingKeepOn = false;
            }
        }

        await StartAsync();
    }

    /// <inheritdoc />
    public async Task StopForSessionAsync()
    {
        _sessionSwitch = false;
        await StartAsync();
    }

    /// <summary>Starts or stops the server to match the settings in effect (after startup and every settings change).</summary>
    /// <returns>A task that completes when the server runs or stopped.</returns>
    public async Task StartAsync()
    {
        await _switch.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            var settings = _services.Settings;
            MainWindow.Agent.IsEnabled = ShouldServe(settings);
            await StopCoreAsync();
            if (!ShouldServe(settings))
            {
                SetStatus(AgentServerStatus.Off, "Off");
                return;
            }

            await StartCoreAsync(settings);
        }
        finally
        {
            _switch.Release();
        }
    }

    /// <inheritdoc />
    public Task<string> ReadTokenAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _token ?? _tokens.LoadOrCreate(), cancellationToken);

    /// <inheritdoc />
    public async Task<string> RegenerateTokenAsync(CancellationToken cancellationToken)
    {
        var token = await Task.Run(_tokens.Regenerate, cancellationToken);
        _token = token;
        _log?.Write("agent: the token was regenerated");
        UseToken(token);
        return token;
    }

    /// <summary>Stops the server, deletes <c>endpoint.json</c>, takes the project back from an agent and stops the tick.</summary>
    /// <returns>A task that completes when everything stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _switch.WaitAsync();
        try
        {
            _disposed = true;
            _timer.Stop();
            _services.SettingsChanged -= OnSettingsChanged;
            await StopCoreAsync();
            _driver.Dispose();
        }
        finally
        {
            _switch.Release();
        }
    }

    private static string? RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Cutlass.slnx")))
            {
                return dir.FullName;
            }
        }

        return null;
    }

    // Published: next to the app. Development: the file in the source tree, when there is one.
    private static string FindBeside(string fileName, string developmentFolder)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(beside))
        {
            return beside;
        }

        if (RepositoryRoot() is { } root && Path.Combine(root, developmentFolder, fileName) is var dev && File.Exists(dev))
        {
            return dev;
        }

        return beside;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _driver.Tick();
        MainWindow.Agent.Refresh();
    }

    private async void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        var (old, now) = (e.OldSettings, e.NewSettings);
        var before = ShouldServe(old);
        if (!_savingKeepOn && old.AgentConnectionsEnabled != now.AgentConnectionsEnabled)
        {
            // The user switched "Allow agent connections" in Settings: that decides again.
            _sessionSwitch = null;
        }

        if (before == ShouldServe(now) && old.AgentPort == now.AgentPort)
        {
            return;
        }

        try
        {
            await StartAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus(AgentServerStatus.Error, "The agent server could not be switched: " + ex.Message);
        }
    }

    private async Task StartCoreAsync(AppSettings settings)
    {
        SetStatus(AgentServerStatus.Starting, "Starting...");
        string token;
        EndpointInfo? other;
        try
        {
            (token, other) = await Task.Run(() => (_token ?? _tokens.LoadOrCreate(), _endpoint.ReadLiveOther()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(AgentServerStatus.Error, "The agent token could not be read: " + ex.Message);
            return;
        }

        _token = token;
        if (other is not null)
        {
            // AGENT_INTEGRATION 4.1: one server per user.
            _log?.Write("agent: another Cutlass (pid " + other.Pid.ToString(CultureInfo.InvariantCulture) + ") serves agents at " + other.BaseUrl);
            SetStatus(AgentServerStatus.OtherWindow, "Another Cutlass window serves agents");
            return;
        }

        var fixedPort = _options.AgentServer && _options.AgentPort is not null || settings.AgentPort != AppSettings.DefaultAgentPort;
        var port = _options.AgentServer && _options.AgentPort is { } forced ? forced : settings.AgentPort;
        var ports = fixedPort ? [port] : Enumerable.Range(port, PortProbeCount + 1).Where(p => p <= 65535).ToArray();
        _dispatcher ??= new ToolDispatcher(_host, _audit, appVersion: SettingsViewModel.AppVersion);
        var outcome = await ListenAsync(ports, token);
        if (outcome.BaseUrl is not { } baseUrl)
        {
            _dispatcher?.Dispose();
            _dispatcher = null;
            SetStatus(outcome.Status, outcome.Message);
            _log?.Write("agent: the server did not start: " + outcome.Message);
            return;
        }

        _baseUrl = baseUrl;
        await WriteEndpointAsync();
        SetStatus(AgentServerStatus.Listening, "Listening on " + baseUrl);
        _log?.Write("agent: listening on " + baseUrl);
    }

    private async Task StopCoreAsync()
    {
        if (MainWindow.Agent.IsLocked && !_disposed && !ShouldServe(_services.Settings))
        {
            // Connections were turned off while an agent held the project: the user gets it back.
            _host.TakeOver();
        }
        else if (_disposed && MainWindow.Agent.IsLocked)
        {
            _host.TakeOver();
        }

        var wasRunning = IsRunning;
        await StopListeningAsync();
        _dispatcher?.Dispose();
        _dispatcher = null;
        _baseUrl = null;
        if (wasRunning)
        {
            try
            {
                await Task.Run(_endpoint.DeleteIfOwned);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log?.Write("agent: endpoint.json could not be deleted: " + ex.Message);
            }
        }
    }

    private async Task WriteEndpointAsync()
    {
        if (_baseUrl is not { } baseUrl || _disposed)
        {
            return;
        }

        var info = EndpointFile.ForThisProcess(baseUrl, "app", _tokens.TokenFile, MainWindow.ProjectPath);
        try
        {
            await Task.Run(() => _endpoint.Write(info));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Write("agent: endpoint.json could not be written: " + ex.Message);
        }
    }

    private void SetStatus(AgentServerStatus status, string text)
    {
        Status = status;
        StatusText = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
