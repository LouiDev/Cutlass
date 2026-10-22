using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Jobs;
using Cutlass.Media.Settings;

namespace Cutlass.Media.Services;

/// <summary>
/// Builds, loads and attaches 540-line proxies (TECH_BRIEF 6.6): intra-only MJPEG (LGPL-safe), video only, written
/// as <c>proxy540.mov</c> with its frame index <c>proxy540.idx</c> in the media cache. Every proxy is indexed and
/// compared with the source index: a different frame count is <c>rejected</c> (the original stays in use), equal
/// timestamps (each minus its file's format start) are <c>exact</c>, anything else <c>ordinal</c>. A usable proxy is
/// attached with <see cref="MediaCatalog.SetProxy"/>, so the compiler seeks it by the source ordinal. Preview audio
/// always comes from the original. Thread-safe.
/// </summary>
public sealed class ProxyService
{
    /// <summary>The proxy height in lines.</summary>
    public const int Height = 540;

    /// <summary>The Auto policy builds a proxy above this display height.</summary>
    public const int AutoMaxHeight = 1080;

    /// <summary>The Auto policy builds a proxy above this bitrate (50 Mb/s).</summary>
    public const long AutoMaxBitRate = 50_000_000;

    /// <summary>How long one proxy encode may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromHours(6);

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly FfmpegCapabilities _capabilities;
    private readonly WorkDirectory _work;
    private readonly MediaCache _cache;
    private readonly FrameIndexService _frameIndex;
    private readonly ProbeService _probe;
    private readonly MediaCatalog _catalog;

    /// <summary>Creates the service.</summary>
    /// <param name="runner">Runs ffmpeg and ffprobe.</param>
    /// <param name="toolset">The validated tools.</param>
    /// <param name="capabilities">The capabilities (encoder time base keyword).</param>
    /// <param name="work">The work directory processes run in.</param>
    /// <param name="cache">The media cache.</param>
    /// <param name="frameIndex">Indexes sources and proxies.</param>
    /// <param name="probe">Probes proxies for their time base and format start.</param>
    /// <param name="catalog">Receives the proxies.</param>
    public ProxyService(
        ProcessRunner runner,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        WorkDirectory work,
        MediaCache cache,
        FrameIndexService frameIndex,
        ProbeService probe,
        MediaCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(frameIndex);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(catalog);
        _runner = runner;
        _toolset = toolset;
        _capabilities = capabilities;
        _work = work;
        _cache = cache;
        _frameIndex = frameIndex;
        _probe = probe;
        _catalog = catalog;
    }

    /// <summary>True for media a proxy can be built for: a video stream that is not a still.</summary>
    /// <param name="info">The media info.</param>
    /// <returns>True when a proxy is possible.</returns>
    public static bool CanBuild(MediaInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.Video is not null && !info.IsStill;
    }

    /// <summary>
    /// The Auto policy: a display height above 1080, an hevc, av1 or vp9 codec, a VFR source, or a bitrate above
    /// 50 Mb/s.
    /// </summary>
    /// <param name="info">The media info.</param>
    /// <returns>True when Auto builds a proxy.</returns>
    public static bool AutoWants(MediaInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!CanBuild(info))
        {
            return false;
        }

        var video = info.Video!;
        var codec = video.Codec.ToLowerInvariant();
        return video.DisplayHeight > AutoMaxHeight
            || codec is "hevc" or "h265" or "av1" or "vp9"
            || video.IsVfr
            || video.BitRate > AutoMaxBitRate;
    }

    /// <summary>Whether the policy builds a proxy for an asset.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="policy">The policy from the settings.</param>
    /// <returns>True when a proxy should be built.</returns>
    public static bool ShouldBuild(MediaAsset asset, ProxyPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return policy switch
        {
            ProxyPolicy.Always => CanBuild(asset.Info),
            ProxyPolicy.Never => false,
            _ => AutoWants(asset.Info),
        };
    }

    /// <summary>
    /// The proxy arguments (TECH_BRIEF 6.6): <c>-hide_banner -nostdin -v error -y -i file -map 0:v -an -sn
    /// -fps_mode passthrough -enc_time_base demux|-1 -video_track_timescale den
    /// -vf scale=w=-2:h=540:flags=bilinear,format=yuvj420p -c:v mjpeg -q:v 4 -progress pipe:1 -nostats output</c>.
    /// </summary>
    /// <param name="file">The source (one argument, never quoted).</param>
    /// <param name="videoStreamIndex">The absolute video stream index.</param>
    /// <param name="encoderTimeBase">The <c>-enc_time_base</c> keyword (<c>demux</c> or <c>-1</c>), or null to omit it.</param>
    /// <param name="trackTimescale">The source time base denominator, or 0 to omit the option.</param>
    /// <param name="output">The output path.</param>
    /// <returns>The arguments, without the executable.</returns>
    public static IReadOnlyList<string> BuildArguments(string file, int videoStreamIndex, string? encoderTimeBase, long trackTimescale, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        List<string> args =
        [
            "-hide_banner", "-nostdin", "-v", "error", "-y", "-i", file, "-map", "0:" + FfNum.Integer(videoStreamIndex),
            "-an", "-sn", "-fps_mode", "passthrough",
        ];
        if (encoderTimeBase is not null)
        {
            args.AddRange(["-enc_time_base", encoderTimeBase]);
        }

        if (trackTimescale > 0)
        {
            args.AddRange(["-video_track_timescale", FfNum.Integer(trackTimescale)]);
        }

        args.AddRange(
        [
            "-vf", $"scale=w=-2:h={Height}:flags=bilinear,format=yuvj420p", "-c:v", "mjpeg", "-q:v", "4",
            "-progress", "pipe:1", "-nostats", output,
        ]);
        return args;
    }

    /// <summary>
    /// Classifies a proxy against its source (TECH_BRIEF 6.6): <c>rejected</c> when the frame counts differ,
    /// <c>exact</c> when every timestamp is equal after subtracting each file's format start, else <c>ordinal</c>.
    /// Without a source index (the CFR fallback) the counts cannot be compared, so an indexed proxy is
    /// <c>ordinal</c>: the compiler's CFR ordinal then picks the proxy frame by position.
    /// </summary>
    /// <param name="source">The source index, or null.</param>
    /// <param name="sourceStartMicros">The source format start.</param>
    /// <param name="proxy">The proxy index, or null when the proxy could not be indexed.</param>
    /// <param name="proxyStartMicros">The proxy format start.</param>
    /// <returns>The mapping.</returns>
    public static ProxyMapping Classify(FrameIndex? source, long sourceStartMicros, FrameIndex? proxy, long proxyStartMicros)
    {
        if (proxy is null)
        {
            return ProxyMapping.Rejected;
        }

        if (source is null)
        {
            return ProxyMapping.Ordinal;
        }

        if (source.Count != proxy.Count)
        {
            return ProxyMapping.Rejected;
        }

        // Exact comparison over the common denominator srcDen * pDen * 1e6.
        const long M = 1_000_000;
        var s = source.TimeBase;
        var p = proxy.TimeBase;
        var sourceOffset = (Int128)sourceStartMicros * s.Den * p.Den;
        var proxyOffset = (Int128)proxyStartMicros * p.Den * s.Den;
        for (var i = 0; i < source.Count; i++)
        {
            var a = ((Int128)source[i] * s.Num * p.Den * M) - sourceOffset;
            var b = ((Int128)proxy[i] * p.Num * s.Den * M) - proxyOffset;
            if (a != b)
            {
                return ProxyMapping.Ordinal;
            }
        }

        return ProxyMapping.Exact;
    }

    /// <summary>
    /// Attaches a proxy that is already in the cache (exact or ordinal) to the catalog. Never encodes.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <param name="cancellationToken">Cancels the probe of the proxy.</param>
    /// <returns>The attached proxy, or null when none is cached.</returns>
    public async Task<RenderSource?> LoadCachedAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!CanBuild(asset.Info))
        {
            return null;
        }

        var source = await Task.Run(() => ReadCached(asset.Fingerprint), cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return null;
        }

        var (path, index) = source.Value;
        try
        {
            var probe = await _probe.ProbeAsync(path, cancellationToken).ConfigureAwait(false);
            var proxy = new RenderSource(path, probe.Info.FormatStartMicros, index);
            _catalog.SetProxy(asset.Id, proxy);
            return proxy;
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or UnauthorizedAccessException)
        {
            // An unreadable proxy is rebuilt by the next proxy job.
            return null;
        }
    }

    /// <summary>
    /// Returns the asset's proxy, building it first when the cache has none, and attaches a usable one to the
    /// catalog. A proxy rejected before is not rebuilt until the proxy artifact version changes.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <param name="progress">Receives fractions from 0 to 1, or null.</param>
    /// <param name="cancellationToken">Cancels (kills) the encode.</param>
    /// <returns>The mapping and the attached proxy (null when rejected).</returns>
    /// <exception cref="InvalidOperationException">When the asset has no video or is a still.</exception>
    /// <exception cref="FfmpegException">When ffmpeg fails.</exception>
    public async Task<(ProxyMapping Mapping, RenderSource? Source)> EnsureAsync(MediaAsset asset, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!CanBuild(asset.Info))
        {
            throw new InvalidOperationException("Only video media that is not a still can have a proxy.");
        }

        var fingerprint = asset.Fingerprint;
        var manifest = await Task.Run(() => _cache.ReadManifest(fingerprint), cancellationToken).ConfigureAwait(false);
        if (manifest is not null && manifest.HasCurrent(CacheArtifacts.Proxy))
        {
            if (manifest.ProxyMapping == ProxyMapping.Rejected)
            {
                return (ProxyMapping.Rejected, null);
            }

            if (await LoadCachedAsync(asset, cancellationToken).ConfigureAwait(false) is { } loaded)
            {
                progress?.Report(1);
                return (manifest.ProxyMapping ?? ProxyMapping.Ordinal, loaded);
            }
        }

        return await BuildAsync(asset, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Detaches and deletes an asset's proxy ("Delete proxy").</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>A task that completes when the files are gone.</returns>
    public Task DeleteAsync(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        _catalog.SetProxy(asset.Id, null);
        return Task.Run(() =>
        {
            _cache.RemoveArtifact(asset.Fingerprint, CacheArtifacts.Proxy, CacheArtifacts.ProxyFile, CacheArtifacts.ProxyIndexFile);
            _cache.UpdateManifest(asset.Fingerprint, m => m with { ProxyMapping = null });
        });
    }

    private (string Path, FrameIndex? Index)? ReadCached(string fingerprint)
    {
        var manifest = _cache.ReadManifest(fingerprint);
        if (manifest is null || !manifest.HasCurrent(CacheArtifacts.Proxy) || manifest.ProxyMapping is not (ProxyMapping.Exact or ProxyMapping.Ordinal))
        {
            return null;
        }

        var movie = _cache.TryGetArtifact(fingerprint, CacheArtifacts.Proxy, CacheArtifacts.ProxyFile);
        var indexPath = _cache.PathOf(fingerprint, CacheArtifacts.ProxyIndexFile);
        if (movie is null || !FrameIndexService.TryReadIndexFile(indexPath, out _, out var index) || index is null)
        {
            return null;
        }

        return (movie, index);
    }

    private async Task<(ProxyMapping Mapping, RenderSource? Source)> BuildAsync(MediaAsset asset, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var video = asset.Info.Video!;
        var fingerprint = asset.Fingerprint;
        var sourceIndex = await _frameIndex.GetAsync(asset, cancellationToken).ConfigureAwait(false);

        _work.EnsureCreated();
        var movieTemp = _cache.NewTempFile(fingerprint, "proxy540", ".mov");
        var indexTemp = _cache.NewTempFile(fingerprint, "proxy540", ".idx");
        try
        {
            // Deviation from TECH_BRIEF 6.6 (doc updated): the encoder time base is kept for every source, not only
            // flagged VFR ones. The VFR flag compares r_frame_rate with avg_frame_rate, which misses VFR files whose
            // average equals the nominal rate (the VFR oracle); their proxy then got 1/25 timestamps and mapped only
            // by ordinal, which put sample-and-hold one frame off. With it every tested source maps exact.
            var args = BuildArguments(
                asset.Path,
                video.StreamIndex,
                _capabilities.EncoderTimeBaseKeyword,
                video.TimeBase.IsPositive ? video.TimeBase.Den : 0,
                movieTemp);
            var spec = new ProcessSpec(_toolset.FfmpegPath, args, _work.Path, RedirectStdin: false, ProcessPurpose.Background, BelowNormalPriority: true);

            // The encode is 99% of the work; probing and indexing the proxy is the last percent.
            var encodeProgress = progress is null ? null : new Progress99(progress);
            await JobProcess.RunAsync(_runner, spec, Math.Max(1, asset.Info.DurationMicros), encodeProgress, Timeout, cancellationToken)
                .ConfigureAwait(false);

            var probe = await _probe.ProbeAsync(movieTemp, cancellationToken).ConfigureAwait(false);
            _probe.Invalidate(movieTemp);
            FrameIndex? proxyIndex = null;
            if (probe.Info.Video is { } proxyVideo)
            {
                proxyIndex = await _frameIndex.GetAsync(movieTemp, proxyVideo.StreamIndex, proxyVideo.TimeBase, cancellationToken).ConfigureAwait(false);
                _frameIndex.Invalidate(movieTemp);
            }

            var mapping = Classify(sourceIndex, asset.Info.FormatStartMicros, proxyIndex, probe.Info.FormatStartMicros);
            if (mapping == ProxyMapping.Rejected)
            {
                // Recorded with the artifact version, so a rejected proxy is retried only after a version bump.
                _cache.Commit(fingerprint, CacheArtifacts.Proxy, [], m => m with { ProxyMapping = ProxyMapping.Rejected });
                _catalog.SetProxy(asset.Id, null);
                progress?.Report(1);
                return (ProxyMapping.Rejected, null);
            }

            FrameIndexService.WriteIndexFile(indexTemp, proxyIndex!.TimeBase, proxyIndex);
            _cache.Commit(
                fingerprint,
                CacheArtifacts.Proxy,
                [(movieTemp, CacheArtifacts.ProxyFile), (indexTemp, CacheArtifacts.ProxyIndexFile)],
                m => m with { ProxyMapping = mapping });

            var final = _cache.PathOf(fingerprint, CacheArtifacts.ProxyFile);
            var proxy = new RenderSource(final, probe.Info.FormatStartMicros, proxyIndex);
            _catalog.SetProxy(asset.Id, proxy);
            progress?.Report(1);
            return (mapping, proxy);
        }
        finally
        {
            TryDelete(movieTemp);
            TryDelete(indexTemp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The startup cleanup removes abandoned temps.
        }
    }

    private sealed class Progress99(IProgress<double> inner) : IProgress<double>
    {
        public void Report(double value) => inner.Report(Math.Clamp(value, 0, 1) * 0.99);
    }
}
