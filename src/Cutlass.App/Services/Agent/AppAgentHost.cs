using Avalonia.Threading;
using Cutlass.Agent.Api;
using Cutlass.Agent.Hosting;
using Cutlass.Agent.Lock;
using Cutlass.App.ViewModels;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Media.Export;
using Cutlass.Media.Platform;

namespace Cutlass.App.Services.Agent;

/// <summary>
/// The App's <see cref="IAgentHost"/> (AGENT_INTEGRATION 2.4) over <see cref="AppServices"/> and the
/// <see cref="MainWindowViewModel"/>. User state: a gesture is the session's gesture or a timeline drag, a modal is any
/// dialog the <see cref="TrackedDialogService"/> counts, an export is a user-started export that is queued or running,
/// and playing covers both monitors. The host operations (import, new, open, save, export) run the menus' paths on
/// the UI thread without their prompts and recheck the caller's lock right before they commit. It also implements the
/// banner's buttons (<see cref="IAgentLockControls"/>).
/// </summary>
public sealed class AppAgentHost : IAgentHost, IAgentLockControls
{
    private readonly AppServices _services;
    private readonly MainWindowViewModel _vm;
    private readonly TrackedDialogService? _dialogs;
    private readonly Func<long> _nowMs;
    private AgentPolicy _policy;

    /// <summary>Creates the host and its lock (which installs the session's write gate).</summary>
    /// <param name="services">The composition root.</param>
    /// <param name="vm">The main window.</param>
    /// <param name="dialogs">The dialog counter for <see cref="AgentUserState.ModalOpen"/>, or null.</param>
    /// <param name="log">The app log, or null.</param>
    /// <param name="nowMs">The clock in Unix milliseconds, or null for the system clock (the lock driver's).</param>
    /// <param name="leaseOptions">The lock's timing rules, or null for the spec's (a smoke run shortens the paused wait).</param>
    public AppAgentHost(AppServices services, MainWindowViewModel vm, TrackedDialogService? dialogs, AppLog? log, Func<long>? nowMs = null, LeaseOptions? leaseOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(vm);
        _services = services;
        _vm = vm;
        _dialogs = dialogs;
        _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Ui = new AvaloniaUiInvoker();
        Services = new AppAgentServices(services, log);
        Lock = new AgentLock(vm.Session, leaseOptions);
        _policy = AgentPolicy.FromSettings(services.Settings);
        services.SettingsChanged += (_, e) => Volatile.Write(ref _policy, AgentPolicy.FromSettings(e.NewSettings));
        vm.AgentUserStateChanged += (_, _) => UserStateChanged?.Invoke(this, EventArgs.Empty);
        vm.Agent.Controls = this;
    }

    /// <inheritdoc />
    public event EventHandler? UserStateChanged;

    /// <inheritdoc />
    public string Kind => "app";

    /// <inheritdoc />
    public IAgentUiInvoker Ui { get; }

    /// <inheritdoc />
    public IAgentServices Services { get; }

    /// <inheritdoc />
    public AgentPolicy Policy => Volatile.Read(ref _policy);

    /// <inheritdoc />
    public ProjectSession Session => _vm.Session;

    /// <inheritdoc />
    public AgentLock Lock { get; }

    /// <inheritdoc />
    public string? ProjectPath => _vm.ProjectPath;

    /// <inheritdoc />
    public long Playhead
    {
        get => _vm.Timeline.Playhead;
        set => _vm.Timeline.Playhead = Math.Max(0, value);
    }

    /// <inheritdoc />
    public AgentUserState UserState => new(
        _vm.Session.InGesture || _vm.Timeline.IsDragging,
        (_dialogs?.OpenDialogCount ?? 0) > 0,
        _vm.IsUserExportRunning,
        _vm.IsAnyMonitorPlaying);

    /// <summary>The main window this host serves.</summary>
    public MainWindowViewModel MainWindow => _vm;

    /// <inheritdoc />
    public void PrepareForAgent() => _vm.PrepareForAgent();

    /// <inheritdoc />
    public void OnAgentEdit(EditResult result) => _vm.FollowAgentEdit(result);

    /// <inheritdoc />
    public void OnAgentFocus(long frame) => _vm.FollowAgentFocus(frame);

    /// <inheritdoc />
    public void OnLockChanged(AgentLockState state) => _vm.Agent.Apply(state);

    /// <inheritdoc />
    public bool TakeOver() => Lock.Revoke(_nowMs());

    /// <inheritdoc />
    public bool Pause() => Lock.Pause(_nowMs());

    /// <inheritdoc />
    public bool Resume() => Lock.Resume(_nowMs());

    /// <inheritdoc />
    public bool Answer(bool allow) => Lock.Answer(allow, UserState, _nowMs());

    /// <inheritdoc />
    public Task<HostImport> ImportAsync(IReadOnlyList<string> paths, string lockId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return RunOnUiAsync(async () =>
        {
            EnsureHolder(lockId);
            var (imported, report) = await _vm.ImportForAgentAsync(paths, () => Lock.IsHolder(lockId));
            if (report.Stopped)
            {
                throw LockLost(lockId);
            }

            // Files the project already held are reported as imported, in request order (HostImport contract).
            var media = _vm.Session.Current.Media;
            var comparer = _services.Paths.PathComparer;
            var present = report.AlreadyPresent
                .Select(p => media.FirstOrDefault(m => comparer.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(p))))
                .OfType<MediaAsset>();
            var all = present.Concat(imported).DistinctBy(a => a.Id).ToList();
            var failed = report.Failed.Select(f => new HostImportFailure(f.Path, f.Message)).ToList();
            return new HostImport(all, failed, [.. report.Warnings]);
        });
    }

    /// <inheritdoc />
    public Task<HostOutcome> NewProjectAsync(SequenceSettings? settings, string lockId, CancellationToken ct) =>
        ReplaceAsync(null, settings, lockId);

    /// <inheritdoc />
    public Task<HostOutcome> OpenProjectAsync(string path, string lockId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ReplaceAsync(Path.GetFullPath(path), null, lockId);
    }

    /// <inheritdoc />
    public Task<HostOutcome> SaveProjectAsync(string? path, string lockId, CancellationToken ct) =>
        RunOnUiAsync(async () =>
        {
            EnsureHolder(lockId);
            if ((path ?? _vm.ProjectPath) is not { } target)
            {
                return HostOutcome.Fail(new ApiError(
                    ErrorCode.Conflict,
                    "The project is untitled. Call save-as with a path inside the allowed write folders.",
                    new Dictionary<string, string> { ["reason"] = "untitled" }));
            }

            if (!_vm.HasProject)
            {
                return HostOutcome.Fail(new ApiError(ErrorCode.Unavailable, "No project is open. Create or open one first."));
            }

            var full = Path.GetFullPath(target);
            if (await _vm.SaveForAgentAsync(full) is { } error)
            {
                return HostOutcome.Fail(new ApiError(ErrorCode.Internal, "The project could not be saved: " + error));
            }

            return HostOutcome.Ok(full);
        });

    /// <inheritdoc />
    public Task<ExportJob> StartExportAsync(ExportRequest request, string lockId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Ui.InvokeAsync(
            () =>
            {
                EnsureHolder(lockId);
                return _vm.StartExportForAgent(request)
                    ?? throw new ApiException(ErrorCode.Unavailable, Services.NotReadyReason ?? "Export is not available.");
            },
            ct);
    }

    private static async Task<T> RunOnUiAsync<T>(Func<Task<T>> work)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await work();
        }

        return await Dispatcher.UIThread.InvokeAsync(work, DispatcherPriority.Normal);
    }

    private Task<HostOutcome> ReplaceAsync(string? path, SequenceSettings? settings, string lockId) =>
        RunOnUiAsync(async () =>
        {
            EnsureHolder(lockId);
            if (_vm.HasProject && _vm.Session.IsDirty)
            {
                // AGENT_INTEGRATION 3.9: the App never discards the user's work for an agent.
                return HostOutcome.Fail(new ApiError(
                    ErrorCode.Conflict,
                    "The open project has unsaved changes. Save it first (project/save or save-as), or ask your user.",
                    new Dictionary<string, string> { ["reason"] = "unsaved_changes" }));
            }

            var (error, lost) = await _vm.ReplaceProjectForAgentAsync(path, settings, () => Lock.IsHolder(lockId));
            if (lost)
            {
                throw LockLost(lockId);
            }

            if (error is not null)
            {
                return HostOutcome.Fail(new ApiError(ErrorCode.InvalidArgument, error, new Dictionary<string, string> { ["field"] = "path" }));
            }

            var offline = _vm.UntouchedMediaIds.Count;
            IReadOnlyList<string> warnings = offline > 0
                ? [offline.ToString(System.Globalization.CultureInfo.InvariantCulture) + " media file(s) are offline (status.project.offlineMediaIds)."]
                : [];
            return HostOutcome.Ok(_vm.ProjectPath, warnings);
        });

    private void EnsureHolder(string lockId)
    {
        if (!Lock.IsHolder(lockId))
        {
            throw LockLost(lockId);
        }
    }

    // lock_lost with its reason when the id was revoked, expired or released, else lock_required with the holder.
    private ApiException LockLost(string lockId)
    {
        var check = Lock.Check(lockId, _nowMs());
        if (check.Status == LockCheckStatus.Lost && check.LostReason is { } reason)
        {
            var wire = LockCheck.WireReason(reason);
            return new ApiException(
                ErrorCode.LockLost,
                "Your lock ended (" + wire + "). Stop editing and report what you did.",
                new Dictionary<string, string> { ["reason"] = wire });
        }

        return new ApiException(
            ErrorCode.LockRequired,
            "You do not hold the project. Call lock/acquire first.",
            new Dictionary<string, string> { ["holder"] = check.Holder });
    }
}
