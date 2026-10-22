using System.Diagnostics;

namespace Cutlass.Media.Jobs;

/// <summary>
/// Runs background media jobs (TECH_BRIEF 6.7): a priority queue (Visible before Background, then the per-media
/// kind order, then arrival), a concurrency limit (2 background processes, 1 while playback or an export runs),
/// deduplication by media id and kind, cancellation, and progress snapshots. Scrub and playback bypass it.
/// </summary>
/// <remarks>
/// <para>Events are raised in the order the changes happened, on whichever worker thread drains them (never under
/// the scheduler's lock): <see cref="JobChanged"/> for every new snapshot, <see cref="JobFinished"/> once per job
/// end. <see cref="ArtifactReady"/> is posted to the synchronization context given to the constructor (the UI
/// thread) when there is one.</para>
/// <para>While an export runs, running proxy jobs are cancelled and re-queued, and queued proxies wait for the
/// export to end.</para>
/// </remarks>
public sealed class JobScheduler : IAsyncDisposable
{
    /// <summary>The default number of jobs that run at once.</summary>
    public const int DefaultConcurrency = 2;

    /// <summary>The number of jobs that run at once during playback or an export.</summary>
    public const int BusyConcurrency = 1;

    /// <summary>How many finished jobs <see cref="Jobs"/> keeps.</summary>
    public const int FinishedHistory = 100;

    /// <summary>The shortest time between two progress snapshots of one job (10 Hz).</summary>
    public static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(100);

    private readonly object _gate = new();
    private readonly List<Entry> _queued = [];
    private readonly List<Entry> _running = [];
    private readonly List<JobInfo> _finished = [];
    private readonly List<TaskCompletionSource> _idleWaiters = [];
    private readonly Queue<Action> _events = new();
    private readonly SynchronizationContext? _artifactContext;
    private long _nextId;
    private long _nextSequence;
    private int _concurrency;
    private bool _playbackActive;
    private bool _exportActive;
    private bool _disposed;
    private int _draining;

    /// <summary>Creates a scheduler.</summary>
    /// <param name="concurrency">How many jobs run at once outside playback and export (at least 1).</param>
    /// <param name="artifactContext">
    /// Where <see cref="ArtifactReady"/> is raised (pass <see cref="SynchronizationContext.Current"/> on the UI
    /// thread), or null to raise it with the other events.
    /// </param>
    public JobScheduler(int concurrency = DefaultConcurrency, SynchronizationContext? artifactContext = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        _concurrency = concurrency;
        _artifactContext = artifactContext;
    }

    /// <summary>Raised with every new job snapshot, in order, on a worker thread.</summary>
    public event Action<JobInfo>? JobChanged;

    /// <summary>Raised once when a job ends (completed, failed or cancelled), after its final <see cref="JobChanged"/>.</summary>
    public event Action<JobInfo>? JobFinished;

    /// <summary>
    /// Raised with the media id and job kind when a job completed successfully, so views can load the new artifact.
    /// Posted to the constructor's synchronization context when one was given.
    /// </summary>
    public event Action<string, JobKind>? ArtifactReady;

    /// <summary>How many jobs run at once outside playback and export (from settings; at least 1).</summary>
    public int Concurrency
    {
        get
        {
            lock (_gate)
            {
                return _concurrency;
            }
        }

        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            lock (_gate)
            {
                _concurrency = value;
                PumpLocked();
            }

            DrainEvents();
        }
    }

    /// <summary>The limit in force now: <see cref="BusyConcurrency"/> during playback or an export.</summary>
    public int EffectiveConcurrency
    {
        get
        {
            lock (_gate)
            {
                return EffectiveLocked;
            }
        }
    }

    /// <summary>True while preview playback runs (the limit drops to one job).</summary>
    public bool IsPlaybackActive
    {
        get
        {
            lock (_gate)
            {
                return _playbackActive;
            }
        }

        set
        {
            lock (_gate)
            {
                _playbackActive = value;
                PumpLocked();
            }

            DrainEvents();
        }
    }

    /// <summary>
    /// True while an export runs: the limit drops to one job, running proxies are cancelled and re-queued, and queued
    /// proxies wait until it is false again.
    /// </summary>
    public bool IsExportActive
    {
        get
        {
            lock (_gate)
            {
                return _exportActive;
            }
        }

        set
        {
            List<Entry> cancel = [];
            lock (_gate)
            {
                _exportActive = value;
                if (value)
                {
                    foreach (var entry in _running.Where(e => e.Job.Kind == JobKind.Proxy))
                    {
                        entry.Requeue = true;
                        cancel.Add(entry);
                    }
                }

                PumpLocked();
            }

            CancelRunning(cancel);
            DrainEvents();
        }
    }

    /// <summary>The number of queued and running jobs.</summary>
    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _queued.Count + _running.Count;
            }
        }
    }

    /// <summary>Snapshots of the running jobs, then the queued ones in run order, then recent finished ones (newest first).</summary>
    public IReadOnlyList<JobInfo> Jobs
    {
        get
        {
            lock (_gate)
            {
                var list = new List<JobInfo>(_running.Count + _queued.Count + _finished.Count);
                list.AddRange(_running.Select(e => e.Info()));
                list.AddRange(_queued.Order(EntryOrder.Instance).Select(e => e.Info()));
                list.AddRange(Enumerable.Reverse(_finished));
                return list;
            }
        }
    }

    private int EffectiveLocked => _playbackActive || _exportActive ? BusyConcurrency : _concurrency;

    /// <summary>
    /// Queues a job. When a job of the same media and kind is already queued or running, nothing new is queued: the
    /// existing job is returned, and a queued one is raised to <paramref name="priority"/> if that is more urgent.
    /// </summary>
    /// <param name="job">The job.</param>
    /// <param name="priority">The priority.</param>
    /// <returns>The snapshot of the queued (or existing) job.</returns>
    /// <exception cref="ObjectDisposedException">After disposal.</exception>
    public JobInfo Enqueue(MediaJob job, JobPriority priority = JobPriority.Background)
    {
        ArgumentNullException.ThrowIfNull(job);
        JobInfo info;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var existing = _running.Concat(_queued).FirstOrDefault(e =>
                e.Job.Kind == job.Kind && string.Equals(e.Job.MediaId, job.MediaId, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (existing.State == JobState.Queued && priority < existing.Priority)
                {
                    existing.Priority = priority;
                    PublishLocked(existing);
                }

                info = existing.Info();
            }
            else
            {
                var entry = new Entry(++_nextId, job, priority, ++_nextSequence);
                _queued.Add(entry);
                PublishLocked(entry);
                info = entry.Info();
                PumpLocked();
            }
        }

        DrainEvents();
        return info;
    }

    /// <summary>Changes the priority of every queued job of a media item (for example when it scrolls into view).</summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="priority">The new priority.</param>
    public void Prioritize(string mediaId, JobPriority priority)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        lock (_gate)
        {
            foreach (var entry in _queued.Where(e => string.Equals(e.Job.MediaId, mediaId, StringComparison.Ordinal) && e.Priority != priority))
            {
                entry.Priority = priority;
                PublishLocked(entry);
            }
        }

        DrainEvents();
    }

    /// <summary>Cancels a queued or running job.</summary>
    /// <param name="jobId">The job id.</param>
    /// <returns>True when the job was queued or running.</returns>
    public bool Cancel(long jobId)
    {
        List<Entry> cancel = [];
        bool found;
        lock (_gate)
        {
            found = CancelLocked(e => e.Id == jobId, cancel) > 0;
        }

        CancelRunning(cancel);
        DrainEvents();
        return found;
    }

    /// <summary>Cancels every queued or running job of a media item (removed from the project, relinked).</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>The number of jobs cancelled.</returns>
    public int CancelMedia(string mediaId)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        List<Entry> cancel = [];
        int count;
        lock (_gate)
        {
            count = CancelLocked(e => string.Equals(e.Job.MediaId, mediaId, StringComparison.Ordinal), cancel);
        }

        CancelRunning(cancel);
        DrainEvents();
        return count;
    }

    /// <summary>Forgets the finished jobs listed by <see cref="Jobs"/>.</summary>
    public void ClearFinished()
    {
        lock (_gate)
        {
            _finished.Clear();
        }
    }

    /// <summary>Completes when no job is queued or running.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when idle.</returns>
    /// <remarks>It completes after every event raised before that moment, so handlers have seen the final states.</remarks>
    public Task WhenIdleAsync(CancellationToken cancellationToken = default)
    {
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _idleWaiters.Add(waiter);
            CompleteIdleWaitersLocked();
        }

        DrainEvents();
        return waiter.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Cancels every job and waits for the running ones to stop.</summary>
    /// <returns>A task that completes when no job runs.</returns>
    public async ValueTask DisposeAsync()
    {
        Task[] running;
        List<Entry> cancel = [];
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CancelLocked(_ => true, cancel);
            running = [.. _running.Select(e => e.Task).OfType<Task>()];
        }

        CancelRunning(cancel);
        DrainEvents();
        try
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // RunAsync catches everything; this only guards against a task that faulted before its body ran.
        }
    }

    // Cancelling a token runs its callbacks inline, and a job's continuation may then re-enter the lock and change
    // the lists; so running jobs are only collected under the lock and cancelled after it is released.
    private static void CancelRunning(List<Entry> entries)
    {
        foreach (var entry in entries)
        {
            entry.Cancel();
        }
    }

    private int CancelLocked(Func<Entry, bool> match, List<Entry> runningToCancel)
    {
        var count = 0;
        foreach (var entry in _queued.Where(match).ToList())
        {
            _queued.Remove(entry);
            entry.State = JobState.Canceled;
            FinishLocked(entry);
            count++;
        }

        foreach (var entry in _running.Where(match))
        {
            entry.Requeue = false;
            runningToCancel.Add(entry);
            count++;
        }

        if (count > 0)
        {
            PumpLocked();
        }

        return count;
    }

    private void PumpLocked()
    {
        if (_disposed)
        {
            CompleteIdleWaitersLocked();
            return;
        }

        while (_running.Count < EffectiveLocked)
        {
            var next = _queued
                .Where(e => !(_exportActive && e.Job.Kind == JobKind.Proxy))
                .Order(EntryOrder.Instance)
                .FirstOrDefault();
            if (next is null)
            {
                break;
            }

            _queued.Remove(next);
            _running.Add(next);
            next.State = JobState.Running;
            next.Fraction = 0;
            next.Error = null;
            next.LastReport = 0;
            var cts = next.Start();
            PublishLocked(next);
            next.Task = Task.Run(() => RunAsync(next, cts.Token));
        }

        CompleteIdleWaitersLocked();
    }

    private void CompleteIdleWaitersLocked()
    {
        if (_queued.Count == 0 && _running.Count == 0 && _idleWaiters.Count > 0)
        {
            // Completed through the event queue, so a waiter resumes only after the events queued before it.
            TaskCompletionSource[] waiters = [.. _idleWaiters];
            _idleWaiters.Clear();
            _events.Enqueue(() =>
            {
                foreach (var waiter in waiters)
                {
                    waiter.TrySetResult();
                }
            });
        }
    }

    private async Task RunAsync(Entry entry, CancellationToken token)
    {
        Exception? error = null;
        var canceled = false;
        try
        {
            await entry.Job.Work(new JobProgress(this, entry), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (Exception ex)
        {
            // Background jobs never crash the app (TECH_BRIEF 5.3); the failure shows in the Jobs popover.
            error = ex;
        }

        lock (_gate)
        {
            _running.Remove(entry);
            entry.DisposeCancellation();
            if (canceled && entry.Requeue && !_disposed)
            {
                entry.Requeue = false;
                entry.State = JobState.Queued;
                entry.Fraction = 0;
                _queued.Add(entry);
                PublishLocked(entry);
            }
            else
            {
                entry.State = canceled ? JobState.Canceled : error is null ? JobState.Completed : JobState.Failed;
                entry.Fraction = entry.State == JobState.Completed ? 1 : entry.Fraction;
                entry.Error = error?.Message;
                FinishLocked(entry);
            }

            PumpLocked();
        }

        DrainEvents();
    }

    private void FinishLocked(Entry entry)
    {
        var info = entry.Info();
        _finished.Add(info);
        if (_finished.Count > FinishedHistory)
        {
            _finished.RemoveAt(0);
        }

        _events.Enqueue(() => JobChanged?.Invoke(info));
        _events.Enqueue(() => JobFinished?.Invoke(info));
        if (info.State == JobState.Completed)
        {
            var (mediaId, kind) = (info.MediaId, info.Kind);
            if (_artifactContext is { } context)
            {
                _events.Enqueue(() => context.Post(_ => RaiseArtifactReady(mediaId, kind), null));
            }
            else
            {
                _events.Enqueue(() => RaiseArtifactReady(mediaId, kind));
            }
        }
    }

    private void RaiseArtifactReady(string mediaId, JobKind kind)
    {
        try
        {
            ArtifactReady?.Invoke(mediaId, kind);
        }
        catch (Exception)
        {
            // A failing handler must not break the scheduler or the UI loop.
        }
    }

    private void PublishLocked(Entry entry)
    {
        var info = entry.Info();
        _events.Enqueue(() => JobChanged?.Invoke(info));
    }

    private void Report(Entry entry, double fraction)
    {
        if (double.IsNaN(fraction))
        {
            return;
        }

        lock (_gate)
        {
            if (entry.State != JobState.Running)
            {
                return;
            }

            entry.Fraction = Math.Clamp(fraction, 0, 1);
            var now = Stopwatch.GetTimestamp();
            if (entry.LastReport != 0 && Stopwatch.GetElapsedTime(entry.LastReport, now) < ProgressInterval && entry.Fraction < 1)
            {
                return;
            }

            entry.LastReport = now;
            PublishLocked(entry);
        }

        DrainEvents();
    }

    // One thread at a time raises the queued events, in the order they were queued under the lock.
    private void DrainEvents()
    {
        while (true)
        {
            if (Interlocked.CompareExchange(ref _draining, 1, 0) != 0)
            {
                return;
            }

            try
            {
                while (true)
                {
                    Action? next;
                    lock (_gate)
                    {
                        if (!_events.TryDequeue(out next))
                        {
                            break;
                        }
                    }

                    try
                    {
                        next();
                    }
                    catch (Exception)
                    {
                        // A failing handler must not break the scheduler.
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _draining, 0);
            }

            lock (_gate)
            {
                if (_events.Count == 0)
                {
                    return;
                }
            }
        }
    }

    private sealed class Entry(long id, MediaJob job, JobPriority priority, long sequence)
    {
        private CancellationTokenSource? _cts;

        public long Id { get; } = id;

        public MediaJob Job { get; } = job;

        public long Sequence { get; } = sequence;

        public JobPriority Priority { get; set; } = priority;

        public JobState State { get; set; } = JobState.Queued;

        public double Fraction { get; set; }

        public string? Error { get; set; }

        public bool Requeue { get; set; }

        public long LastReport { get; set; }

        public Task? Task { get; set; }

        public JobInfo Info() => new(Id, Job.Kind, Job.MediaId, Job.MediaName, Priority, State, Fraction, Error);

        public CancellationTokenSource Start()
        {
            _cts = new CancellationTokenSource();
            return _cts;
        }

        public void Cancel()
        {
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Finished meanwhile.
            }
        }

        public void DisposeCancellation()
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    private sealed class EntryOrder : IComparer<Entry>
    {
        public static readonly EntryOrder Instance = new();

        public int Compare(Entry? x, Entry? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var c = x.Priority.CompareTo(y.Priority);
            if (c == 0)
            {
                c = x.Job.Kind.CompareTo(y.Job.Kind);
            }

            return c != 0 ? c : x.Sequence.CompareTo(y.Sequence);
        }
    }

    private sealed class JobProgress(JobScheduler owner, Entry entry) : IProgress<double>
    {
        public void Report(double value) => owner.Report(entry, value);
    }
}
