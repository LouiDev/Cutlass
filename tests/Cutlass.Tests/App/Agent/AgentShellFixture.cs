using Cutlass.Agent.Hosting;
using Cutlass.Agent.Lock;
using Cutlass.App;
using Cutlass.App.ViewModels;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Tests.App.Shell;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// A main window view model on temporary folders with an <see cref="AgentLock"/> on its session (which installs the
/// write gate) and a manual clock, for the agent UI tests. The lock state reaches the view model the way the lock
/// driver routes it (<see cref="AgentViewModel.Apply"/> on every change).
/// </summary>
internal sealed class AgentShellFixture : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cutlass-agentui-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the view model, the lock and the clock.</summary>
    public AgentShellFixture()
    {
        var paths = new AppPaths(Path.Combine(_folder, "settings.json"), Path.Combine(_folder, "data"), Path.Combine(_folder, "cache"), OperatingSystem.IsWindows());
        var registry = new ChildProcessRegistry(null);
        Services = new AppServices(paths, registry, new ProcessRunner(registry, null));
        Dialogs = new FakeDialogService();
        Vm = new MainWindowViewModel(Services, Dialogs, action => action());
        Lock = new AgentLock(Vm.Session);
        Lock.Changed += (_, state) => Vm.Agent.Apply(state);
    }

    /// <summary>The composition root.</summary>
    public AppServices Services { get; }

    /// <summary>The dialogs.</summary>
    public FakeDialogService Dialogs { get; }

    /// <summary>The main window.</summary>
    public MainWindowViewModel Vm { get; }

    /// <summary>The lock on the session.</summary>
    public AgentLock Lock { get; }

    /// <summary>The manual clock in Unix milliseconds.</summary>
    public long Now { get; set; } = 1_000_000;

    /// <summary>Grants the lock to an agent.</summary>
    /// <param name="name">The agent's name.</param>
    /// <param name="reason">Its reason.</param>
    /// <returns>The lock id.</returns>
    public string Grant(string name = "Claude Code", string reason = "Remove silences")
    {
        var outcome = Lock.Acquire(new AcquireRequest(name, reason), AgentUserState.Idle, Now);
        Assert.Equal(AcquireStatus.Granted, outcome.Status);
        return outcome.LockId!;
    }

    /// <summary>Removes the temporary folders.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written.
        }
    }
}
