using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Services;

/// <summary>
/// The app's <see cref="IMediaCatalog"/>: every registered asset with its original file and frame index (TECH_BRIEF
/// 7.1). An asset registered without an index gets one in the background: the file is probed (to confirm it is still
/// readable media and to read its current stream time base) and indexed with the packet pass (TECH_BRIEF 6.4); until
/// then seeks use the CFR fallback. Unreadable or missing files answer null, so the compiler treats them as offline.
/// <see cref="MediaRenderInfo.Proxy"/> is null until <see cref="SetProxy"/> attaches one (the proxy service does that
/// after building or loading a proxy, TECH_BRIEF 6.6); re-registering the same media keeps it. Thread-safe.
/// </summary>
public sealed class MediaCatalog : IMediaCatalog, IDisposable
{
    /// <summary>How many background index passes run at once (TECH_BRIEF 6.7: two background processes).</summary>
    public const int BackgroundConcurrency = 2;

    private readonly ProbeService _probe;
    private readonly FrameIndexService _frameIndex;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly SemaphoreSlim _background = new(BackgroundConcurrency, BackgroundConcurrency);
    private readonly CancellationTokenSource _stop = new();
    private long _generation;
    private bool _disposed;

    /// <summary>Creates an empty catalog.</summary>
    /// <param name="probe">Re-probes files before they are indexed in the background.</param>
    /// <param name="frameIndex">Builds frame indexes.</param>
    public MediaCatalog(ProbeService probe, FrameIndexService frameIndex)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(frameIndex);
        _probe = probe;
        _frameIndex = frameIndex;
    }

    /// <summary>
    /// Raised on a worker thread with the media id once a background index pass has finished (with or without an
    /// index), so the viewer can request the current frame again with exact seeks.
    /// </summary>
    public event Action<string>? IndexReady;

    /// <summary>Raised on a worker thread with the media id when <see cref="SetProxy"/> attached or removed a proxy.</summary>
    public event Action<string>? ProxyChanged;

    /// <summary>
    /// Raised with the media id when <see cref="DetachMissingProxies"/> found its proxy file gone (another instance's
    /// eviction or "Clear cache"); the media pipeline queues the proxy again.
    /// </summary>
    public event Action<string>? ProxyLost;

    /// <summary>
    /// Media ids whose files must not be touched (DESIGN 11: media offline or on an unconfirmed remote volume when the
    /// project opened). Such an asset registers offline without a file lookup. Called on any thread under the catalog
    /// lock, so it must be cheap and must not call back into the catalog. Null looks every path up.
    /// </summary>
    public Func<string, bool>? KnownOffline { get; set; }

    /// <summary>The registered assets, in no particular order.</summary>
    public IReadOnlyList<MediaAsset> Assets
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries.Values.Select(e => e.Asset)];
            }
        }
    }

    /// <inheritdoc />
    public MediaRenderInfo? Get(string mediaId)
    {
        if (mediaId is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _entries.TryGetValue(mediaId, out var entry) && entry.Online ? entry.Info : null;
        }
    }

    /// <summary>
    /// Registers or replaces an asset whose index is not known yet and schedules the background index pass. An
    /// unchanged asset (same path and media info) keeps its current index state.
    /// </summary>
    /// <param name="asset">The asset.</param>
    public void Register(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Entry entry;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(asset.Id, out var existing) && SameMedia(existing.Asset, asset))
            {
                _entries[asset.Id] = existing with { Asset = asset, Info = existing.Info with { Asset = asset } };
                return;
            }

            // File.Exists is a metadata lookup; the expensive work (probe and index) runs in the background.
            entry = new Entry(++_generation, asset, Info(asset, null, null), Exists(asset), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            Replace(asset.Id, entry);
        }

        if (!entry.Online || asset.Info.Video is null || asset.Info.IsStill)
        {
            // Offline, audio-only and still media need no index.
            entry.IndexDone.TrySetResult();
            return;
        }

        _ = Task.Run(() => IndexInBackgroundAsync(entry));
    }

    /// <summary>Registers or replaces an asset whose index is already known (the importer builds it).</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="index">The frame index, or null when the CFR fallback applies.</param>
    public void Register(MediaAsset asset, FrameIndex? index)
    {
        ArgumentNullException.ThrowIfNull(asset);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            done.TrySetResult();

            // The same media keeps a proxy attached meanwhile (the proxy depends only on the content).
            var proxy = _entries.TryGetValue(asset.Id, out var existing) && SameMedia(existing.Asset, asset) ? existing.Info.Proxy : null;
            Replace(asset.Id, new Entry(++_generation, asset, Info(asset, index, proxy), Exists(asset), done));
        }
    }

    /// <summary>
    /// Attaches a proxy to a registered asset, or removes it with null, so the compiler renders from it when a
    /// request asks for proxies (TECH_BRIEF 6.6). Raises <see cref="ProxyChanged"/> when something changed.
    /// </summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="proxy">The proxy file with its own format start and frame index, or null.</param>
    /// <returns>True when the asset is registered.</returns>
    public bool SetProxy(string mediaId, RenderSource? proxy)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        lock (_gate)
        {
            if (!_entries.TryGetValue(mediaId, out var entry))
            {
                return false;
            }

            if (Equals(entry.Info.Proxy, proxy))
            {
                return true;
            }

            _entries[mediaId] = entry with { Info = entry.Info with { Proxy = proxy } };
        }

        try
        {
            ProxyChanged?.Invoke(mediaId);
        }
        catch (Exception)
        {
            // A failing handler must not break the catalog.
        }

        return true;
    }

    /// <summary>
    /// Detaches every proxy whose file no longer exists (TECH_BRIEF 6.1: another app instance can evict or clear the
    /// shared cache), so the compiler renders from the original again, and raises <see cref="ProxyLost"/> for each.
    /// Cheap: one existence check per attached proxy.
    /// </summary>
    /// <returns>The media ids whose proxy was detached.</returns>
    public IReadOnlyList<string> DetachMissingProxies()
    {
        List<string> candidates;
        lock (_gate)
        {
            candidates = [.. _entries.Where(e => e.Value.Info.Proxy is not null).Select(e => e.Key)];
        }

        var lost = new List<string>();
        foreach (var id in candidates)
        {
            var proxy = Get(id)?.Proxy;
            if (proxy is null || File.Exists(proxy.Path))
            {
                continue;
            }

            SetProxy(id, null);
            lost.Add(id);
            try
            {
                ProxyLost?.Invoke(id);
            }
            catch (Exception)
            {
                // A failing handler must not break the catalog.
            }
        }

        return lost;
    }

    /// <summary>Forgets an asset.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>True when it was registered.</returns>
    public bool Remove(string mediaId)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        lock (_gate)
        {
            if (!_entries.Remove(mediaId, out var entry))
            {
                return false;
            }

            entry.IndexDone.TrySetResult();
            return true;
        }
    }

    /// <summary>
    /// Makes every asset of the project resolvable: registers the ones that are missing or changed (relinked).
    /// Assets that are no longer in the project stay registered, because undo can bring their clips back; call
    /// <see cref="Remove"/> to drop one.
    /// </summary>
    /// <param name="project">The project.</param>
    public void Sync(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (var asset in project.Media)
        {
            bool current;
            lock (_gate)
            {
                current = _entries.TryGetValue(asset.Id, out var entry) && entry.Asset == asset;
            }

            if (!current)
            {
                Register(asset);
            }
        }
    }

    /// <summary>True when the asset is registered and its file was found.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>True when online.</returns>
    public bool IsOnline(string mediaId)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        lock (_gate)
        {
            return _entries.TryGetValue(mediaId, out var entry) && entry.Online;
        }
    }

    /// <summary>True when the asset's index pass has finished (an index or the known CFR fallback).</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>True when ready.</returns>
    public bool IsIndexReady(string mediaId)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        lock (_gate)
        {
            return _entries.TryGetValue(mediaId, out var entry) && entry.IndexDone.Task.IsCompleted;
        }
    }

    /// <summary>Waits until the asset's index pass has finished. Completes at once for unknown ids.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>A task that completes when the index is ready.</returns>
    public Task WaitForIndexAsync(string mediaId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mediaId);
        Task task;
        lock (_gate)
        {
            task = _entries.TryGetValue(mediaId, out var entry) ? entry.IndexDone.Task : Task.CompletedTask;
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>Cancels the background index passes. Registered assets stay resolvable.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                entry.IndexDone.TrySetResult();
            }
        }

        _stop.Cancel();
        _stop.Dispose();
    }

    // File.Exists, unless the asset's path must not be touched (KnownOffline).
    private bool Exists(MediaAsset asset) => KnownOffline?.Invoke(asset.Id) != true && File.Exists(asset.Path);

    private static bool SameMedia(MediaAsset a, MediaAsset b) =>
        string.Equals(a.Path, b.Path, StringComparison.Ordinal) && a.Info == b.Info && a.Fingerprint == b.Fingerprint;

    private static MediaRenderInfo Info(MediaAsset asset, FrameIndex? index, RenderSource? proxy) =>
        new(asset, new RenderSource(asset.Path, asset.Info.FormatStartMicros, index), proxy);

    private void Replace(string id, Entry entry)
    {
        if (_entries.TryGetValue(id, out var previous))
        {
            previous.IndexDone.TrySetResult();
        }

        _entries[id] = entry;
    }

    private async Task IndexInBackgroundAsync(Entry entry)
    {
        var asset = entry.Asset;
        CancellationToken token;
        try
        {
            token = _stop.Token;
        }
        catch (ObjectDisposedException)
        {
            entry.IndexDone.TrySetResult();
            return;
        }

        var online = true;
        FrameIndex? index = null;
        try
        {
            await _background.WaitAsync(token).ConfigureAwait(false);
            try
            {
                // The probe is memoised by path, size and mtime, so this is free right after an import.
                var probe = await _probe.ProbeAsync(asset.Path, token).ConfigureAwait(false);
                if (probe.Info.Video is { } video && !probe.Info.IsStill)
                {
                    // The asset overload can answer from the media cache's frames.idx (it checks size and mtime).
                    index = asset.Info.Video is { } recorded && recorded.StreamIndex == video.StreamIndex && recorded.TimeBase == video.TimeBase
                        ? await _frameIndex.GetAsync(asset, token).ConfigureAwait(false)
                        : await _frameIndex.GetAsync(asset.Path, video.StreamIndex, video.TimeBase, token).ConfigureAwait(false);
                }
            }
            finally
            {
                _background.Release();
            }
        }
        catch (OperationCanceledException)
        {
            entry.IndexDone.TrySetResult();
            return;
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or UnauthorizedAccessException)
        {
            // Missing or no longer readable: the compiler treats the media as offline instead of failing in ffmpeg.
            online = false;
        }

        lock (_gate)
        {
            if (_entries.TryGetValue(asset.Id, out var current) && current.Generation == entry.Generation)
            {
                // The asset record may have been refreshed meanwhile (same media); keep the newest record.
                _entries[asset.Id] = current with { Info = Info(current.Asset, index, current.Info.Proxy), Online = online };
            }
        }

        entry.IndexDone.TrySetResult();
        try
        {
            IndexReady?.Invoke(asset.Id);
        }
        catch (Exception)
        {
            // A failing handler must not break the catalog.
        }
    }

    // Generation identifies one registration; refreshing an unchanged asset keeps it, so a running pass still lands.
    private sealed record Entry(long Generation, MediaAsset Asset, MediaRenderInfo Info, bool Online, TaskCompletionSource IndexDone);
}
