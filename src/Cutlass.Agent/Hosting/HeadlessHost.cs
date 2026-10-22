using Cutlass.Agent.Api;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Media.Export;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Projects;

namespace Cutlass.Agent.Hosting;

/// <summary>
/// The host without UI (AGENT_INTEGRATION 2.5), for <c>Cutlass.Agent.Mcp --headless</c>, <c>serve</c> and the
/// integration tests. Its "UI thread" is a <see cref="HeadlessDispatcher"/>; the user is always idle, the follow hooks
/// do nothing and the playhead is a field. Only this host accepts <c>discardChanges</c> on new and open. On dispose
/// with unsaved changes it writes an autosave with the owner lock file, so the App offers recovery; it never overwrites
/// the project without an explicit save.
/// </summary>
public sealed class HeadlessHost : IAgentHost, IAsyncDisposable
{
    private readonly HeadlessEngine _engine;
    private readonly HeadlessDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly bool _ownsEngine;
    private ProjectSession? _session;
    private AgentLock? _lock;
    private AgentLockDriver? _driver;
    private string? _projectPath;
    private string? _untitledAutosave;
    private bool _disposed;

    private HeadlessHost(HeadlessEngine engine, HeadlessDispatcher dispatcher, AgentPolicy policy, TimeProvider time, bool ownsEngine)
    {
        _engine = engine;
        _dispatcher = dispatcher;
        _time = time;
        _ownsEngine = ownsEngine;
        Policy = policy;
    }

    /// <inheritdoc />
    public event EventHandler? UserStateChanged
    {
        add
        {
            // The headless user is always idle, so the state never changes.
        }

        remove
        {
        }
    }

    /// <inheritdoc />
    public string Kind => "headless";

    /// <inheritdoc />
    public IAgentUiInvoker Ui => _dispatcher;

    /// <summary>The session thread.</summary>
    public HeadlessDispatcher Dispatcher => _dispatcher;

    /// <inheritdoc />
    public IAgentServices Services => _engine;

    /// <summary>The engine.</summary>
    public HeadlessEngine Engine => _engine;

    /// <inheritdoc />
    public AgentPolicy Policy { get; set; }

    /// <inheritdoc />
    public ProjectSession Session => _session ?? throw new InvalidOperationException("The host is not started.");

    /// <inheritdoc />
    public AgentLock Lock => _lock ?? throw new InvalidOperationException("The host is not started.");

    /// <summary>The lock driver.</summary>
    public AgentLockDriver Driver => _driver ?? throw new InvalidOperationException("The host is not started.");

    /// <inheritdoc />
    public string? ProjectPath => _projectPath;

    /// <inheritdoc />
    public long Playhead { get; set; }

    /// <inheritdoc />
    public AgentUserState UserState => AgentUserState.Idle;

    /// <summary>Starts a host on an engine: loads or creates the project and binds the lock.</summary>
    /// <param name="engine">The engine.</param>
    /// <param name="options">The options.</param>
    /// <param name="ownsEngine">True to dispose the engine with the host.</param>
    /// <param name="ct">Cancels startup.</param>
    /// <returns>The host.</returns>
    /// <exception cref="FileNotFoundException">When the project is missing and <see cref="HeadlessHostOptions.Create"/> is false.</exception>
    /// <exception cref="InvalidDataException">When the project file cannot be read.</exception>
    public static async Task<HeadlessHost> CreateAsync(HeadlessEngine engine, HeadlessHostOptions options, bool ownsEngine = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        var policy = AgentPolicy.FromSettings(engine.Settings, options.ReadFolders, options.WriteFolders, acceptsDiscardChanges: true);
        var host = new HeadlessHost(engine, new HeadlessDispatcher(), policy, options.Time ?? TimeProvider.System, ownsEngine);
        Project project;
        string? path = null;
        if (options.ProjectPath is { } given)
        {
            path = Path.GetFullPath(given);
            if (File.Exists(path))
            {
                project = ProjectStore.WithNativePaths(await engine.Projects.LoadAsync(path, ct).ConfigureAwait(false));
            }
            else if (options.Create)
            {
                project = TimelineEdits.NewProject(engine.Ids);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await engine.Projects.SaveAsync(project, path, ct).ConfigureAwait(false);
            }
            else
            {
                await host._dispatcher.InvokeAsync(() => host._dispatcher.Dispose(), CancellationToken.None).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                throw new FileNotFoundException("The project file does not exist; pass --create to make it.", path);
            }
        }
        else
        {
            project = TimelineEdits.NewProject(engine.Ids);
        }

        engine.Catalog?.Sync(project);
        await host._dispatcher.InvokeAsync(
            () =>
            {
                host._session = new ProjectSession(project, () => host._time.GetTimestamp() * 1000 / host._time.TimestampFrequency);
                host._projectPath = path;
                host._lock = new AgentLock(host._session, options.Lease);
                host._driver = new AgentLockDriver(host, host._time);
                if (options.StartLockDriver)
                {
                    host._driver.Start();
                }
            },
            ct).ConfigureAwait(false);
        return host;
    }

    /// <summary>Starts an engine on this machine's paths and a host on it, owning both.</summary>
    /// <param name="options">The host options.</param>
    /// <param name="ffmpegPath">An ffmpeg to prefer, or null.</param>
    /// <param name="ct">Cancels startup.</param>
    /// <returns>The host.</returns>
    public static async Task<HeadlessHost> StartAsync(HeadlessHostOptions options, string? ffmpegPath = null, CancellationToken ct = default)
    {
        var engine = await HeadlessEngine.StartAsync(null, ffmpegPath, null, installShutdownHooks: true, ct).ConfigureAwait(false);
        try
        {
            return await CreateAsync(engine, options, ownsEngine: true, ct).ConfigureAwait(false);
        }
        catch
        {
            await engine.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <inheritdoc />
    public void PrepareForAgent()
    {
        // No inline editors without a UI.
    }

    /// <inheritdoc />
    public void OnAgentEdit(EditResult result)
    {
        // Follow does nothing without a UI.
    }

    /// <inheritdoc />
    public void OnAgentFocus(long frame)
    {
        // Follow does nothing without a UI; set_playhead sets the field itself.
    }

    /// <inheritdoc />
    public void OnLockChanged(AgentLockState state)
    {
        // No banner without a UI.
    }

    /// <inheritdoc />
    public async Task<HostImport> ImportAsync(IReadOnlyList<string> paths, string lockId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var importer = _engine.Importer ?? throw new ApiException(ErrorCode.Unavailable, "ffmpeg is not ready: " + _engine.NotReadyReason);
        var existing = await _dispatcher.InvokeAsync(() => Session.Current.Media, ct).ConfigureAwait(false);
        var comparison = _engine.Paths.PathComparison;
        var imported = new List<MediaAsset>();
        var fresh = new List<MediaAsset>();
        var failed = new List<HostImportFailure>();
        var warnings = new List<string>();
        foreach (var path in paths)
        {
            var known = existing.FirstOrDefault(m => string.Equals(Path.GetFullPath(m.Path), Path.GetFullPath(path), comparison))
                ?? fresh.FirstOrDefault(m => string.Equals(m.Path, Path.GetFullPath(path), comparison));
            if (known is not null)
            {
                imported.Add(known);
                continue;
            }

            try
            {
                var result = await importer.ImportAsync(path, ct).ConfigureAwait(false);
                imported.Add(result.Asset);
                fresh.Add(result.Asset);
                if (result.Warning is { } w)
                {
                    warnings.Add(Path.GetFileName(path) + ": " + w);
                }
            }
            catch (Exception ex) when (ex is FfmpegException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                failed.Add(new HostImportFailure(path, ex is FfmpegException ? "ffprobe could not read the file; it is not a supported media file." : ex.Message));
            }
        }

        if (fresh.Count > 0)
        {
            await _dispatcher.InvokeAsync(
                () =>
                {
                    RequireLock(lockId);
                    using (Session.WriteAs(SessionWriter.Agent))
                    {
                        Session.Execute(p =>
                        {
                            var r = TimelineEdits.ImportMedia(p, fresh);
                            return r with { Label = EditRunner.LabelPrefix + r.Label };
                        });
                    }
                },
                ct).ConfigureAwait(false);
        }

        return new HostImport(imported, failed, warnings);
    }

    /// <inheritdoc />
    public async Task<HostOutcome> NewProjectAsync(SequenceSettings? settings, string lockId, CancellationToken ct)
    {
        var project = TimelineEdits.NewProject(_engine.Ids, settings);
        await _dispatcher.InvokeAsync(
            () =>
            {
                RequireLock(lockId);
                using (Session.WriteAs(SessionWriter.Agent))
                {
                    Session.Replace(project);
                }

                _projectPath = null;
                _untitledAutosave = null;
                Playhead = 0;
            },
            ct).ConfigureAwait(false);
        return HostOutcome.Ok(null);
    }

    /// <inheritdoc />
    public async Task<HostOutcome> OpenProjectAsync(string path, string lockId, CancellationToken ct)
    {
        Project project;
        try
        {
            project = ProjectStore.WithNativePaths(await _engine.Projects.LoadAsync(path, ct).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return HostOutcome.Fail(new ApiError(ErrorCode.InvalidArgument, "The project could not be read: " + ex.Message, new Dictionary<string, string> { ["field"] = "path" }));
        }

        _engine.Catalog?.Sync(project);
        await _dispatcher.InvokeAsync(
            () =>
            {
                RequireLock(lockId);
                using (Session.WriteAs(SessionWriter.Agent))
                {
                    Session.Replace(project);
                }

                _projectPath = Path.GetFullPath(path);
                Playhead = 0;
            },
            ct).ConfigureAwait(false);
        var offline = project.Media.Where(m => _engine.Catalog is { } c && !c.IsOnline(m.Id)).Select(m => m.Id).ToList();
        return HostOutcome.Ok(_projectPath, offline.Count == 0 ? null : [$"Offline media (file missing): {string.Join(", ", offline)}."]);
    }

    /// <inheritdoc />
    public async Task<HostOutcome> SaveProjectAsync(string? path, string lockId, CancellationToken ct)
    {
        var (project, current) = await _dispatcher.InvokeAsync(
            () =>
            {
                RequireLock(lockId);
                return (Session.Current, _projectPath);
            },
            ct).ConfigureAwait(false);
        var target = path ?? current;
        if (target is null)
        {
            return HostOutcome.Fail(new ApiError(ErrorCode.Conflict, "The project has no file yet. Use save-as.", new Dictionary<string, string> { ["reason"] = "untitled" }));
        }

        try
        {
            await _engine.Projects.SaveAsync(project, target, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HostOutcome.Fail(new ApiError(ErrorCode.Forbidden, "The project could not be written: " + ex.Message, new Dictionary<string, string> { ["path"] = target }));
        }

        await _dispatcher.InvokeAsync(
            () =>
            {
                if (ReferenceEquals(Session.Current, project))
                {
                    Session.MarkSaved();
                }

                _projectPath = target;
            },
            ct).ConfigureAwait(false);
        return HostOutcome.Ok(target);
    }

    /// <inheritdoc />
    public async Task<ExportJob> StartExportAsync(ExportRequest request, string lockId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var export = _engine.Export ?? throw new ApiException(ErrorCode.Unavailable, "ffmpeg is not ready: " + _engine.NotReadyReason);
        await _dispatcher.InvokeAsync(() => RequireLock(lockId), ct).ConfigureAwait(false);
        return export.Enqueue(request);
    }

    /// <summary>Stops the lock driver, writes an autosave when the project has unsaved changes, and stops the session thread.</summary>
    /// <returns>A task that completes when stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        (Project Project, string? Path, bool Dirty)? state = null;
        try
        {
            state = await _dispatcher.InvokeAsync(
                () =>
                {
                    _driver?.Dispose();
                    return _session is { } s ? (s.Current, _projectPath, s.IsDirty) : default((Project, string?, bool)?);
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The dispatcher already stopped.
        }

        if (state is { Dirty: true } s2)
        {
            await WriteAutosaveAsync(s2.Project, s2.Path).ConfigureAwait(false);
        }

        _dispatcher.Dispose();
        if (_ownsEngine)
        {
            await _engine.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task WriteAutosaveAsync(Project project, string? projectPath)
    {
        try
        {
            var target = projectPath is not null
                ? ProjectStore.AutosavePath(projectPath)
                : _untitledAutosave ??= AutosaveRecovery.UntitledPath(_engine.Paths.AutosaveDirectory, Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            // The owner lock names this process, which is gone when the App looks, so the App offers recovery (TECH_BRIEF 10.1).
            await File.WriteAllTextAsync(AutosaveRecovery.LockPath(target), AutosaveRecovery.CurrentOwner()).ConfigureAwait(false);
            await _engine.Projects.SaveAutosaveAsync(project, target, projectPath).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _engine.Log?.WriteException("headless autosave", ex);
        }
    }

    private void RequireLock(string lockId)
    {
        var check = Lock.Check(lockId, _time.GetUtcNow().ToUnixTimeMilliseconds());
        if (check.Status is LockCheckStatus.Required or LockCheckStatus.Lost)
        {
            throw LockErrors.From(check, Lock.State);
        }
    }
}
