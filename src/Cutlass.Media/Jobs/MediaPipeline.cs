using Cutlass.Core.Model;
using Cutlass.Media.Cache;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;

namespace Cutlass.Media.Jobs;

/// <summary>
/// Turns media items into scheduler jobs (TECH_BRIEF 6.7): frame index, thumbnails, waveform and, where the proxy
/// policy asks for one, a proxy. Every job answers from the media cache when its artifact exists, so re-queueing a
/// whole project on launch is cheap and finishes what an earlier session left undone. After each job the cache LRU
/// runs, never evicting media the catalog knows (the open project, including media only undo can bring back).
/// </summary>
public sealed class MediaPipeline : IDisposable
{
    private readonly JobScheduler _scheduler;
    private readonly MediaCache _cache;
    private readonly MediaCatalog _catalog;
    private readonly FrameIndexService _frameIndex;
    private readonly ThumbnailService _thumbnails;
    private readonly WaveformService _waveforms;
    private readonly ProxyService _proxies;
    private int _evicting;
    private bool _disposed;

    /// <summary>Creates the pipeline and hooks the LRU to the scheduler.</summary>
    /// <param name="scheduler">Runs the jobs.</param>
    /// <param name="cache">The media cache.</param>
    /// <param name="catalog">The catalog (protected media, proxies).</param>
    /// <param name="frameIndex">Builds frame indexes.</param>
    /// <param name="thumbnails">Renders thumbnail sheets.</param>
    /// <param name="waveforms">Builds peak pyramids.</param>
    /// <param name="proxies">Builds proxies.</param>
    public MediaPipeline(
        JobScheduler scheduler,
        MediaCache cache,
        MediaCatalog catalog,
        FrameIndexService frameIndex,
        ThumbnailService thumbnails,
        WaveformService waveforms,
        ProxyService proxies)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(frameIndex);
        ArgumentNullException.ThrowIfNull(thumbnails);
        ArgumentNullException.ThrowIfNull(waveforms);
        ArgumentNullException.ThrowIfNull(proxies);
        _scheduler = scheduler;
        _cache = cache;
        _catalog = catalog;
        _frameIndex = frameIndex;
        _thumbnails = thumbnails;
        _waveforms = waveforms;
        _proxies = proxies;
        _scheduler.JobFinished += OnJobFinished;
        _catalog.ProxyLost += OnProxyLost;
    }

    /// <summary>When proxies are built (settings). Changing it affects jobs queued afterwards.</summary>
    public ProxyPolicy ProxyPolicy { get; set; } = ProxyPolicy.Auto;

    /// <summary>
    /// The fingerprints the LRU must keep, or null to protect every asset the catalog knows (the default).
    /// </summary>
    public Func<IReadOnlyCollection<string>>? ProtectedFingerprints { get; set; }

    /// <summary>The fingerprints protected from eviction right now.</summary>
    /// <returns>The fingerprints.</returns>
    public IReadOnlyCollection<string> CurrentProtected() =>
        ProtectedFingerprints?.Invoke() ?? [.. _catalog.Assets.Select(a => a.Fingerprint)];

    /// <summary>
    /// Queues every job a media item needs: the frame index for video, thumbnails for video, the waveform for audio
    /// and a proxy when <see cref="ProxyPolicy"/> asks for one. A proxy built earlier is attached either way. Jobs
    /// already queued or running are not duplicated; a more urgent priority raises them.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <param name="priority">The priority.</param>
    /// <returns>The queued (or existing) jobs.</returns>
    public IReadOnlyList<JobInfo> Enqueue(MediaAsset asset, JobPriority priority = JobPriority.Background)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var name = Path.GetFileName(asset.Path);
        var jobs = new List<JobInfo>();
        var info = asset.Info;
        if (info.Video is not null && !info.IsStill)
        {
            jobs.Add(_scheduler.Enqueue(new MediaJob(JobKind.FrameIndex, asset.Id, name, async (_, ct) => await _frameIndex.GetAsync(asset, ct).ConfigureAwait(false)), priority));
        }

        if (info.Video is not null)
        {
            jobs.Add(_scheduler.Enqueue(new MediaJob(JobKind.Thumbnails, asset.Id, name, async (p, ct) => await _thumbnails.GetSheetAsync(asset, p, ct).ConfigureAwait(false)), priority));
        }

        if (info.Audio is not null)
        {
            jobs.Add(_scheduler.Enqueue(new MediaJob(JobKind.Waveform, asset.Id, name, async (p, ct) => await _waveforms.GetAsync(asset, p, ct).ConfigureAwait(false)), priority));
        }

        if (ProxyService.ShouldBuild(asset, ProxyPolicy))
        {
            jobs.Add(EnqueueProxy(asset, priority));
        }
        else if (ProxyService.CanBuild(info))
        {
            // Not wanted by the policy, but one built earlier (or by "Create proxy") is still used.
            _ = Task.Run(() => _proxies.LoadCachedAsync(asset, CancellationToken.None));
        }

        return jobs;
    }

    /// <summary>Queues the jobs of every media item of a project (on launch and after opening a project).</summary>
    /// <param name="project">The project.</param>
    /// <param name="priority">The priority.</param>
    public void EnqueueProject(Project project, JobPriority priority = JobPriority.Background)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (var asset in project.Media)
        {
            Enqueue(asset, priority);
        }
    }

    /// <summary>Queues a proxy whatever the policy ("Create proxy" in the media bin).</summary>
    /// <param name="asset">The asset (video, not a still).</param>
    /// <param name="priority">The priority.</param>
    /// <returns>The queued (or existing) job.</returns>
    public JobInfo EnqueueProxy(MediaAsset asset, JobPriority priority = JobPriority.Background)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _scheduler.Enqueue(
            new MediaJob(JobKind.Proxy, asset.Id, Path.GetFileName(asset.Path), async (p, ct) => await _proxies.EnsureAsync(asset, p, ct).ConfigureAwait(false)),
            priority);
    }

    /// <summary>Runs the startup cache work off the UI thread: abandoned temps, the size measure and the LRU.</summary>
    /// <param name="cancellationToken">Cancels before the work starts.</param>
    /// <returns>A task that completes when done.</returns>
    public Task RunStartupMaintenanceAsync(CancellationToken cancellationToken = default) =>
        Task.Run(
            () =>
            {
                _cache.DeleteStaleTemps();
                _cache.Evict(CurrentProtected());
            },
            cancellationToken);

    /// <summary>The fingerprints of the media a running job works on (they survive "Clear cache").</summary>
    /// <returns>The fingerprints.</returns>
    public IReadOnlyCollection<string> BusyFingerprints()
    {
        var running = _scheduler.Jobs.Where(j => j.State == JobState.Running).Select(j => j.MediaId).ToHashSet(StringComparer.Ordinal);
        return [.. _catalog.Assets.Where(a => running.Contains(a.Id)).Select(a => a.Fingerprint).Distinct(StringComparer.Ordinal)];
    }

    /// <summary>
    /// "Clear cache" (DESIGN 10): detaches the proxies of media no running job uses, then deletes their cache folders
    /// (<see cref="MediaCache.ClearAsync"/>). Queue the project again afterwards (<see cref="EnqueueProject"/>) to
    /// rebuild what the open project needs.
    /// </summary>
    /// <param name="cancellationToken">Cancels before the next folder.</param>
    /// <returns>The keys of the deleted entries.</returns>
    public async Task<IReadOnlyList<string>> ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        var busy = BusyFingerprints();
        var busySet = busy.ToHashSet(StringComparer.Ordinal);
        foreach (var asset in _catalog.Assets.Where(a => !busySet.Contains(a.Fingerprint)))
        {
            // The proxy file is about to go; the compiler must render from the original again.
            _catalog.SetProxy(asset.Id, null);
            _thumbnails.Forget(asset.Fingerprint);
        }

        return await _cache.ClearAsync(busy, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Unhooks the scheduler.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scheduler.JobFinished -= OnJobFinished;
        _catalog.ProxyLost -= OnProxyLost;
    }

    // A proxy another instance deleted (TECH_BRIEF 6.1) is built again; the original serves meanwhile.
    private void OnProxyLost(string mediaId)
    {
        if (_disposed || _catalog.Get(mediaId)?.Asset is not { } asset)
        {
            return;
        }

        try
        {
            EnqueueProxy(asset, JobPriority.Background);
        }
        catch (ObjectDisposedException)
        {
            // Shutting down.
        }
    }

    private void OnJobFinished(JobInfo info)
    {
        if (info.State != JobState.Completed || Interlocked.Exchange(ref _evicting, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                _cache.Evict(CurrentProtected());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The next job retries.
            }
            finally
            {
                Volatile.Write(ref _evicting, 0);
            }
        });
    }
}
