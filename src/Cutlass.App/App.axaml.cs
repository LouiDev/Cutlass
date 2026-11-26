using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Cutlass.Agent.Lock;
using Cutlass.App.Services;
using Cutlass.App.Services.Agent;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.App.Services.Dialogs;
using Cutlass.App.Services.Smoke;
using Cutlass.App.ViewModels;
using Cutlass.App.Views;

namespace Cutlass.App;

/// <summary>The Avalonia application: loads the theme and builds the composition root.</summary>
public sealed partial class App : Application
{
    /// <summary>A smoke run that has not finished after this long exits with code 1.</summary>
    private static readonly TimeSpan SmokeWatchdog = TimeSpan.FromMinutes(20);

    /// <summary>The composition root, available once the framework has initialized.</summary>
    public AppServices? Services { get; private set; }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = CommandLineOptions.Parse(desktop.Args);
            var services = AppServices.CreateDefault();
            Services = services;
            var window = new MainWindow();

            // A smoke run (--smoke-script) answers every dialog itself, so nothing waits for a person.
            SmokeLog? smokeLog = null;
            SmokeDialogService? smokeDialogs = null;
            if (options.SmokeScriptPath is { } script)
            {
                smokeLog = new SmokeLog(options.SmokeLogPath ?? script + ".log");
                smokeDialogs = new SmokeDialogService(smokeLog);
            }

            // The tracker counts open dialogs for the agent lock's "modal" busy state (AGENT_INTEGRATION 2.4).
            var dialogs = new TrackedDialogService(smokeDialogs ?? (IDialogService)new AvaloniaDialogService(() => window));
            var vm = new MainWindowViewModel(services, dialogs);
            InstallCrashHandlers(vm);

            // AGENT_INTEGRATION 2.4: the host and its lock exist from the start; the server listens once the settings are
            // loaded, when "Allow agent connections" or --agent-server says so. A smoke run lets a paused call wait 1 s.
            var leaseOptions = options.SmokeScriptPath is null ? null : LeaseOptions.Default with { PausedWaitMs = 1000 };
            var agentHost = new AppAgentHost(services, vm, dialogs, vm.Log, leaseOptions: leaseOptions);
            var agentServer = new AgentServerController(services, agentHost, options, vm.Log);
            vm.AgentServer = agentServer;

            // AGENT_INTEGRATION 9.3: the agent clients come from the profile file beside the exe and the user profiles in
            // <data>/agent/clients. Client CLIs run through CliProcess (never ProcessRunner), off the UI thread. A smoke
            // run uses a sandbox home next to its script and a runner that starts nothing, so it never touches a real
            // client configuration (the smoke command "settings agent-home=<dir>" moves the sandbox).
            vm.ClientRegistry = options.SmokeScriptPath is { } smokeScript
                ? AgentClientRegistry.ForSandbox(
                    Path.Combine(Path.GetDirectoryName(Path.GetFullPath(smokeScript)) ?? ".", "agent-clients-home"),
                    new SmokeCliRunner(smokeLog is null ? null : smokeLog.Write))
                : new AgentClientRegistry(
                    AgentClientEnvironment.Current(),
                    new CliProcess(),
                    Path.Combine(AppContext.BaseDirectory, AgentClientRegistry.ProfileFileName),
                    Path.Combine(services.Paths.AgentDirectory, "clients"),
                    Path.Combine(services.Paths.AgentDirectory, "clients.state.json"));
            vm.AgentRuntime = agentServer;
            window.DataContext = vm;
            desktop.MainWindow = window;

            // Startup I/O (settings, ffmpeg discovery, capability probe) runs off the UI thread once the window shows.
            window.Opened += async (_, _) =>
            {
                await vm.InitializeAsync(options);
                await agentServer.StartAsync();
                if (smokeLog is not null && options.SmokeScriptPath is { } path)
                {
                    await RunSmokeAsync(desktop, window, vm, services, smokeDialogs, smokeLog, path, agentServer);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunSmokeAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        MainWindow window,
        MainWindowViewModel vm,
        AppServices services,
        SmokeDialogService? dialogs,
        SmokeLog log,
        string script,
        AgentServerController agentServer)
    {
        _ = Task.Run(async () =>
        {
            // Last resort when a step hangs the UI thread: the process still ends with a failure code.
            await Task.Delay(SmokeWatchdog).ConfigureAwait(false);
            log.Write("SMOKE FAIL: the script did not finish within " + SmokeWatchdog.TotalMinutes + " minutes");
            await log.DisposeAsync().ConfigureAwait(false);
            Environment.Exit(1);
        });

        int code;
        try
        {
            code = await new SmokeRunner(vm, services, dialogs, window.CaptureAsync, log)
            {
                CaptureDialog = (dialog, path, height) => WindowCapture.CaptureDialogAsync(window, dialog, path, height),
                CaptureMenu = (header, path, submenus) => WindowCapture.CaptureMenuAsync(window, header, path, submenus),
                AgentServer = agentServer,
            }.RunFileAsync(script);
        }
        catch (Exception ex)
        {
            log.Write("SMOKE FAIL: " + ex);
            code = 1;
        }

        await vm.ShutdownWithoutPromptAsync();
        log.Write("exit code " + code);
        await log.DisposeAsync();
        desktop.Shutdown(code);
    }
}
