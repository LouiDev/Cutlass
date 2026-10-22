using Avalonia.Threading;
using Cutlass.Core.Session;
using Cutlass.Media.Projects;

namespace Cutlass.App.Services;

/// <summary>
/// Autosaves the open project while it is dirty (DESIGN 11). The project is written to
/// <c>&lt;name&gt;.cutlass.autosave</c> next to a saved project, or to <c>&lt;data&gt;/autosave/untitled-&lt;id&gt;.cutlass</c>
/// for an untitled one, atomically through <see cref="ProjectStore"/>.
/// </summary>
/// <remarks>
/// <para><b>When.</b> A periodic check (every <see cref="TickPeriod"/>) saves when the project has edits that are not
/// autosaved yet, no gesture or drag is running, and either <see cref="IdleDelay"/> (30 s) passed since the last edit
/// or the oldest unsaved edit is one <see cref="Interval"/> old (so continuous editing still autosaves). Autosaves are
/// at least one interval apart ("at most once per interval"). An interval of zero disables autosave.</para>
/// <para><b>Threads.</b> The check runs on the UI thread and only snapshots the immutable project reference; the
/// serialization and the write run on the thread pool. Writes and deletes are serialised, and a generation number
/// drops a write that finished after the project was saved, closed or replaced.</para>
/// <para><b>Clearing.</b> A successful save, a clean close and replacing the project (after the save prompt) delete
/// the autosaves this service wrote; a save also deletes an older autosave of the same project.</para>
/// <para><b>Owner locks.</b> Every write first writes <c>&lt;autosave&gt;.lock</c> with this process's id and start time
/// (<see cref="AutosaveRecovery.CurrentOwner"/>), and deleting an autosave deletes its lock, so another instance never
/// offers this instance's live autosave for recovery. A crash leaves a lock naming a dead process, which recovery
/// ignores.</para>
/// </remarks>
public sealed class AutosaveService : IDisposable
{
    /// <summary>How long after the last edit an autosave runs (DESIGN 11: 30 s).</summary>
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(30);

    /// <summary>How often the timer checks whether an autosave is due.</summary>
    public static readonly TimeSpan TickPeriod = TimeSpan.FromSeconds(5);

    private readonly ProjectStore _store;
    private readonly string _untitledDirectory;
    private readonly ProjectSession _session;
    private readonly Func<string?> _projectPath;
    private readonly Func<bool> _isSuppressed;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<TimeSpan, Action, IDisposable> _startTimer;
    private readonly Func<string> _newId;
    private readonly string _lockOwner;
    private readonly SemaphoreSlim _io = new(1, 1);
    private readonly object _gate = new();
    private readonly List<string> _written = [];
    private IDisposable? _timer;
    private long _generation;
    private bool _pending;
    private bool _writing;
    private DateTime? _firstPending;
    private DateTime? _lastEdit;
    private DateTime? _lastWrite;
    private string? _untitledPath;
    private bool _disposed;

    /// <summary>Creates the service. Nothing is written until <see cref="Start"/> runs the timer.</summary>
    /// <param name="store">Writes the autosave files.</param>
    /// <param name="untitledDirectory">Where untitled projects autosave (<c>AppPaths.AutosaveDirectory</c>).</param>
    /// <param name="session">The project session (UI thread).</param>
    /// <param name="projectPath">The open project's file, or null while untitled or when no project is open.</param>
    /// <param name="isSuppressed">True while a gesture or drag runs (no autosave mid-gesture).</param>
    /// <param name="utcNow">The clock, or null for <see cref="DateTime.UtcNow"/>.</param>
    /// <param name="startTimer">Starts a periodic callback and returns its stopper, or null for a UI thread
    /// <see cref="DispatcherTimer"/>.</param>
    /// <param name="newId">Creates the id of an untitled autosave, or null for a random one.</param>
    /// <param name="lockOwner">The owner written to the autosave locks, or null for
    /// <see cref="AutosaveRecovery.CurrentOwner"/>.</param>
    public AutosaveService(
        ProjectStore store,
        string untitledDirectory,
        ProjectSession session,
        Func<string?> projectPath,
        Func<bool> isSuppressed,
        Func<DateTime>? utcNow = null,
        Func<TimeSpan, Action, IDisposable>? startTimer = null,
        Func<string>? newId = null,
        string? lockOwner = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(untitledDirectory);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(isSuppressed);
        _store = store;
        _untitledDirectory = untitledDirectory;
        _session = session;
        _projectPath = projectPath;
        _isSuppressed = isSuppressed;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _startTimer = startTimer ?? StartDispatcherTimer;
        _newId = newId ?? (() => Guid.NewGuid().ToString("N")[..12]);
        _lockOwner = lockOwner ?? AutosaveRecovery.CurrentOwner();
        _session.Changed += OnSessionChanged;
    }

    /// <summary>Raised (on the thread that awaited the write) when an autosave could not be written.</summary>
    public event Action<Exception>? WriteFailed;

    /// <summary>The autosave interval (settings: <c>AutosaveIntervalMinutes</c>); zero or less disables autosave.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>True when autosave is on.</summary>
    public bool IsEnabled => Interval > TimeSpan.Zero;

    /// <summary>True while edits wait for an autosave.</summary>
    public bool HasPendingEdits
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <summary>How many autosaves this instance wrote.</summary>
    public int WriteCount { get; private set; }

    /// <summary>The file the next autosave of the open project goes to.</summary>
    public string TargetPath
    {
        get
        {
            if (_projectPath() is { } path)
            {
                return ProjectStore.AutosavePath(path);
            }

            lock (_gate)
            {
                return _untitledPath ??= AutosaveRecovery.UntitledPath(_untitledDirectory, _newId());
            }
        }
    }

    /// <summary>Converts the settings value to an interval (zero or less disables).</summary>
    /// <param name="minutes">The interval in minutes.</param>
    /// <returns>The interval.</returns>
    public static TimeSpan IntervalFromMinutes(int minutes) => minutes <= 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(minutes);

    /// <summary>
    /// The autosave rule (see the class remarks). Pure, for tests: due when autosave is on, edits wait, nothing
    /// suppresses it, the last autosave is at least one interval ago, and the project was idle for
    /// <see cref="IdleDelay"/> or the oldest waiting edit is one interval old.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="interval">The interval (zero disables).</param>
    /// <param name="pending">True when edits wait for an autosave.</param>
    /// <param name="suppressed">True during a gesture, a drag or a running write.</param>
    /// <param name="firstPending">When the oldest waiting edit happened.</param>
    /// <param name="lastEdit">When the newest edit happened.</param>
    /// <param name="lastWrite">When the last autosave was written, or null.</param>
    /// <returns>True when an autosave should run now.</returns>
    public static bool IsDue(
        DateTime now,
        TimeSpan interval,
        bool pending,
        bool suppressed,
        DateTime? firstPending,
        DateTime? lastEdit,
        DateTime? lastWrite)
    {
        if (interval <= TimeSpan.Zero || !pending || suppressed)
        {
            return false;
        }

        if (lastWrite is { } written && now - written < interval)
        {
            return false;
        }

        var idle = lastEdit is not { } edit || now - edit >= IdleDelay;
        var overdue = firstPending is { } first && now - first >= interval;
        return idle || overdue;
    }

    /// <summary>Starts the periodic check (once).</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _timer ??= _startTimer(TickPeriod, () => _ = TickAsync());
    }

    /// <summary>Stops the periodic check.</summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// The periodic check (UI thread): writes an autosave when one is due (<see cref="IsDue(DateTime, TimeSpan, bool, bool, DateTime?, DateTime?, DateTime?)"/>).
    /// </summary>
    /// <returns>True when an autosave was written.</returns>
    public Task<bool> TickAsync()
    {
        bool due;
        var now = _utcNow();
        lock (_gate)
        {
            due = IsDue(now, Interval, _pending, _writing || _isSuppressed(), _firstPending, _lastEdit, _lastWrite);
        }

        return due ? WriteAsync(now) : Task.FromResult(false);
    }

    /// <summary>Writes an autosave now if the project has edits that are not autosaved (whatever the timing).</summary>
    /// <returns>True when an autosave was written.</returns>
    public Task<bool> SaveNowAsync()
    {
        bool pending;
        lock (_gate)
        {
            pending = _pending && !_writing;
        }

        return pending ? WriteAsync(_utcNow()) : Task.FromResult(false);
    }

    /// <summary>
    /// The crash path (a terminating exception or a termination signal): writes the dirty project synchronously, best
    /// effort, from any thread. It waits at most <paramref name="timeout"/> (for a running write too, since writes are
    /// serialised) and never throws. The file counts as written by this service, so a later clean close deletes it.
    /// </summary>
    /// <param name="timeout">How long to wait for the write.</param>
    /// <returns>True when the autosave was written.</returns>
    public bool TrySaveNowBlocking(TimeSpan timeout)
    {
        try
        {
            if (!IsEnabled || !_session.IsDirty)
            {
                return false;
            }

            // Reading the reference from another thread is safe: the model is immutable.
            var project = _session.Current;
            var path = _projectPath();
            var target = TargetPath;
            // The one deliberate blocking wait of the app (M7 review): the process is going down, so there is no
            // later to await in. The write runs on the thread pool (no dispatcher, so no deadlock) and the wait is
            // bounded by the timeout.
            var write = Task.Run(async () =>
            {
                if (!await _io.WaitAsync(timeout).ConfigureAwait(false))
                {
                    return false;
                }

                try
                {
                    WriteLock(target);
                    await _store.SaveAutosaveAsync(project, target, path).ConfigureAwait(false);
                }
                finally
                {
                    _io.Release();
                }

                lock (_gate)
                {
                    if (!_written.Contains(target, StringComparer.Ordinal))
                    {
                        _written.Add(target);
                    }
                }

                return true;
            });
            return write.Wait(timeout) && write.Result;
        }
        catch (Exception)
        {
            // Best effort while the process is going down.
            return false;
        }
    }

    /// <summary>
    /// Deletes the autosaves this service wrote for the open project and forgets waiting edits (after a successful
    /// save, a clean close, or when the project is replaced). With <paramref name="includeProjectAutosave"/> the
    /// project's <c>.cutlass.autosave</c> goes too even when another session wrote it (a save makes it obsolete).
    /// </summary>
    /// <param name="includeProjectAutosave">True after a save.</param>
    /// <returns>A task that completes when the files are deleted (never faults).</returns>
    public async Task ClearAsync(bool includeProjectAutosave)
    {
        List<string> targets;
        var now = _utcNow();
        lock (_gate)
        {
            _generation++;

            // Edits made while a save was written stay pending.
            _pending = _session.IsDirty;
            _firstPending = _pending ? now : null;
            _lastEdit = _pending ? now : null;
            _lastWrite = null;
            targets = [.. _written];
            _written.Clear();
            if (_untitledPath is { } untitled)
            {
                // A saved untitled project now has a name; a closed one needs no autosave.
                targets.Add(untitled);
                _untitledPath = null;
            }
        }

        if (includeProjectAutosave && _projectPath() is { } path)
        {
            targets.Add(ProjectStore.AutosavePath(path));
        }

        await DeleteAsync(targets).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes over an existing untitled autosave (a recovered untitled project), so later autosaves overwrite it and a
    /// save or clean close deletes it.
    /// </summary>
    /// <param name="autosavePath">The untitled autosave file.</param>
    public void Adopt(string autosavePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autosavePath);
        var full = Path.GetFullPath(autosavePath);
        lock (_gate)
        {
            _untitledPath = full;
            if (!_written.Contains(full, StringComparer.Ordinal))
            {
                _written.Add(full);
            }
        }

        // Claim it at once: until the next autosave another instance would otherwise offer it for recovery.
        _ = LockAsync(full);
    }

    /// <summary>Stops the timer and unhooks the session.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _session.Changed -= OnSessionChanged;
    }

    private static IDisposable StartDispatcherTimer(TimeSpan period, Action tick)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = period };
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new TimerStopper(timer);
    }

    private void OnSessionChanged(object? sender, ProjectChangedEventArgs e)
    {
        var now = _utcNow();
        List<string>? obsolete = null;
        lock (_gate)
        {
            switch (e.Kind)
            {
                case ChangeKind.Saved:
                    if (!_session.IsDirty)
                    {
                        _pending = false;
                        _firstPending = null;
                    }

                    break;
                case ChangeKind.Replace:
                    // Another project (open, new, recover) after the save prompt: the old autosaves are obsolete.
                    _generation++;
                    obsolete = [.. _written];
                    _written.Clear();
                    _untitledPath = null;
                    _pending = _session.IsDirty;
                    _firstPending = _pending ? now : null;
                    _lastEdit = _pending ? now : null;
                    _lastWrite = null;
                    break;
                default:
                    // Edits, gesture updates, undo and redo: the content changed.
                    _lastEdit = now;
                    if (_session.IsDirty)
                    {
                        _firstPending ??= now;
                        _pending = true;
                    }
                    else
                    {
                        // Undo back to the saved state: nothing to autosave.
                        _pending = false;
                        _firstPending = null;
                    }

                    break;
            }
        }

        if (obsolete is { Count: > 0 })
        {
            _ = DeleteAsync(obsolete);
        }
    }

    private async Task<bool> WriteAsync(DateTime now)
    {
        var project = _session.Current;
        var path = _projectPath();
        var target = TargetPath;
        long generation;
        DateTime? firstPending;
        lock (_gate)
        {
            generation = _generation;
            firstPending = _firstPending;
            _writing = true;
            _pending = false;
            _firstPending = null;
            _lastWrite = now;
        }

        try
        {
            var written = await Task.Run(async () =>
            {
                await _io.WaitAsync().ConfigureAwait(false);
                try
                {
                    lock (_gate)
                    {
                        if (generation != _generation)
                        {
                            return false;
                        }
                    }

                    WriteLock(target);
                    await _store.SaveAutosaveAsync(project, target, path).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (generation == _generation && !_written.Contains(target, StringComparer.Ordinal))
                        {
                            _written.Add(target);
                        }
                    }

                    return true;
                }
                finally
                {
                    _io.Release();
                }
            }).ConfigureAwait(true);
            if (written)
            {
                WriteCount++;
            }

            return written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    // Try again at the next due check.
                    _pending = true;
                    _firstPending ??= firstPending ?? now;
                }
            }

            WriteFailed?.Invoke(ex);
            return false;
        }
        finally
        {
            lock (_gate)
            {
                _writing = false;
            }
        }
    }

    private async Task DeleteAsync(IReadOnlyList<string> paths)
    {
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                foreach (var file in paths.Distinct(StringComparer.Ordinal))
                {
                    try
                    {
                        _store.DeleteAutosave(file);
                        File.Delete(AutosaveRecovery.LockPath(file));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Left behind: recovery offers it only while it is newer than the project.
                    }
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            _io.Release();
        }
    }

    // Runs inside the _io section, before the autosave itself, so the autosave never exists without its lock.
    private void WriteLock(string autosavePath)
    {
        var lockPath = AutosaveRecovery.LockPath(autosavePath);
        if (Path.GetDirectoryName(lockPath) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(lockPath, _lockOwner);
    }

    private async Task LockAsync(string autosavePath)
    {
        await _io.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() => WriteLock(autosavePath)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The next autosave writes it again.
        }
        finally
        {
            _io.Release();
        }
    }

    private sealed class TimerStopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
