using System.Collections.Concurrent;
using System.Text.Json;
using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Jobs;

namespace Cutlass.Media.Services;

/// <summary>
/// Renders and caches the thumbnail sprite sheets of a media item (TECH_BRIEF 6.5):
/// <c>ffmpeg -hide_banner -nostdin -v error [-skip_frame nokey if &gt; 10 min] -i &lt;proxy or file&gt; -map 0:&lt;v&gt; -an -sn
/// -vf fps=1/&lt;interval&gt;:round=up,scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2,tile=10x10
/// -q:v 5 -progress pipe:1 -nostats -y thumbs.tmp-&lt;guid8&gt;/sheet_%03d.jpg</c>, with
/// <c>interval = max(1 s, duration/600)</c> and <c>count = ceil(duration/interval)</c>. The sheets and
/// <c>thumbs.json</c> live in the media cache's <c>thumbs/</c> folder. Thread-safe.
/// </summary>
/// <remarks>
/// <c>round=up</c> deviates from the brief's original command (doc updated): the fps filter keeps the last input frame
/// that rounds into each output slot, so the default <c>round=near</c> showed the frame just before
/// <c>(i + 0.5) * interval</c>; <c>round=up</c> shows the frame with the largest pts at or before <c>i * interval</c>,
/// the same sample-and-hold rule as the compiler (TECH_BRIEF 7.2).
/// </remarks>
public sealed class ThumbnailService
{
    /// <summary>Media longer than this decode key frames only (10 minutes).</summary>
    public const long KeyframesOnlyAboveMicros = 600_000_000;

    /// <summary>The shortest interval between thumbnails.</summary>
    public const long MinIntervalMicros = 1_000_000;

    /// <summary>The most thumbnails per media item at the minimum density (duration / 600).</summary>
    public const int TargetCount = 600;

    /// <summary>How long one render may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(60);

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly WorkDirectory _work;
    private readonly MediaCache _cache;
    private readonly MediaCatalog? _catalog;
    private readonly ConcurrentDictionary<string, ThumbnailSheet> _known = new(StringComparer.Ordinal);

    /// <summary>Creates the service.</summary>
    /// <param name="runner">Runs ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="work">The work directory ffmpeg runs in.</param>
    /// <param name="cache">The media cache.</param>
    /// <param name="catalog">The catalog, to decode from a proxy when one is attached, or null.</param>
    public ThumbnailService(ProcessRunner runner, FfmpegToolset toolset, WorkDirectory work, MediaCache cache, MediaCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(cache);
        _runner = runner;
        _toolset = toolset;
        _work = work;
        _cache = cache;
        _catalog = catalog;
    }

    /// <summary>The interval between thumbnails: <c>max(1 s, duration / 600)</c>.</summary>
    /// <param name="durationMicros">The media duration.</param>
    /// <returns>The interval in microseconds.</returns>
    public static long IntervalMicros(long durationMicros) => Math.Max(MinIntervalMicros, durationMicros / TargetCount);

    /// <summary>The number of thumbnails: <c>ceil(duration / interval)</c>, at least 1 (stills).</summary>
    /// <param name="durationMicros">The media duration.</param>
    /// <param name="intervalMicros">The interval.</param>
    /// <returns>The count.</returns>
    public static int TileCount(long durationMicros, long intervalMicros)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(intervalMicros);
        return (int)Math.Max(1, (Math.Max(0, durationMicros) + intervalMicros - 1) / intervalMicros);
    }

    /// <summary>The thumbnail arguments (see the class summary).</summary>
    /// <param name="input">The proxy or the original file (one argument, never quoted).</param>
    /// <param name="videoStreamIndex">The absolute video stream index in <paramref name="input"/>.</param>
    /// <param name="intervalMicros">The interval.</param>
    /// <param name="keyframesOnly">True for media longer than 10 minutes (<c>-skip_frame nokey</c>).</param>
    /// <param name="outputPattern">The <c>.../sheet_%03d.jpg</c> pattern.</param>
    /// <returns>The arguments, without the executable.</returns>
    public static IReadOnlyList<string> BuildArguments(string input, int videoStreamIndex, long intervalMicros, bool keyframesOnly, string outputPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPattern);
        List<string> args = ["-hide_banner", "-nostdin", "-v", "error"];
        if (keyframesOnly)
        {
            args.AddRange(["-skip_frame", "nokey"]);
        }

        var w = ThumbnailSheet.TileWidth;
        var h = ThumbnailSheet.TileHeight;
        args.AddRange(
        [
            "-i", input, "-map", "0:" + FfNum.Integer(videoStreamIndex), "-an", "-sn",
            "-vf", $"fps=1/{FfNum.Seconds(intervalMicros)}:round=up,scale={w}:{h}:force_original_aspect_ratio=decrease," +
                $"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2,tile={ThumbnailSheet.Columns}x{ThumbnailSheet.Rows}",
            "-q:v", "5", "-progress", "pipe:1", "-nostats", "-y", outputPattern,
        ]);
        return args;
    }

    /// <summary>The sheets of an asset if they were rendered or loaded already. Memory only, so safe on the UI thread.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The sheets, or null.</returns>
    public ThumbnailSheet? TryGet(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _known.TryGetValue(asset.Fingerprint, out var sheet) ? sheet : null;
    }

    /// <summary>Forgets the sheets known for a fingerprint (after "Clear cache" deleted them).</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    public void Forget(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        _known.TryRemove(fingerprint, out _);
    }

    /// <summary>Returns the sheets of an asset from the cache, rendering them first when missing.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="progress">Receives fractions from 0 to 1, or null.</param>
    /// <param name="cancellationToken">Cancels the render.</param>
    /// <returns>The sheets, or null for audio-only media.</returns>
    /// <exception cref="FfmpegException">When ffmpeg fails.</exception>
    public async Task<ThumbnailSheet?> GetSheetAsync(MediaAsset asset, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Info.Video is not { } video)
        {
            return null;
        }

        if (TryGet(asset) is { } known && File.Exists(known.SheetPath(0)))
        {
            return known;
        }

        var fingerprint = asset.Fingerprint;
        var cached = await Task.Run(() => Load(fingerprint), cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _known[fingerprint] = cached;
            return cached;
        }

        var duration = Math.Max(0, asset.Info.DurationMicros);
        var interval = IntervalMicros(duration);
        var count = TileCount(duration, interval);

        // Decoding the 540-line proxy is cheaper than the original; its only stream is the video.
        var proxy = _catalog?.Get(asset.Id)?.Proxy;
        var input = proxy is not null && File.Exists(proxy.Path) ? proxy.Path : asset.Path;
        var stream = proxy is not null && input == proxy.Path ? 0 : video.StreamIndex;

        var temp = _cache.NewTempDirectory(fingerprint, CacheArtifacts.ThumbnailsFolder);
        try
        {
            _work.EnsureCreated();
            var args = BuildArguments(input, stream, interval, duration > KeyframesOnlyAboveMicros, Path.Combine(temp, "sheet_%03d.jpg"));
            var spec = new ProcessSpec(_toolset.FfmpegPath, args, _work.Path, RedirectStdin: false, ProcessPurpose.Background, BelowNormalPriority: true);
            await JobProcess.RunAsync(_runner, spec, Math.Max(1, duration), progress, Timeout, cancellationToken).ConfigureAwait(false);

            var sheet = new ThumbnailSheet(temp, interval, count);
            var produced = 0;
            while (produced < sheet.SheetCount && File.Exists(sheet.SheetPath(produced)))
            {
                produced++;
            }

            if (produced == 0)
            {
                throw new FfmpegException("ffmpeg produced no thumbnail sheet.", 0, [], ProcessRunner.FormatDisplayCommand(spec.FileName, spec.Args, !OperatingSystem.IsWindows()));
            }

            // A reported duration past the last frame can leave the last sheets unwritten; lookups clamp to what exists.
            count = Math.Min(count, produced * ThumbnailSheet.TilesPerSheet);

            var file = new ThumbnailIndexFile
            {
                IntervalSec = interval / 1e6,
                TileW = ThumbnailSheet.TileWidth,
                TileH = ThumbnailSheet.TileHeight,
                Cols = ThumbnailSheet.Columns,
                Rows = ThumbnailSheet.Rows,
                Count = count,
            };
            await File.WriteAllBytesAsync(
                Path.Combine(temp, CacheArtifacts.ThumbnailsIndexFile),
                JsonSerializer.SerializeToUtf8Bytes(file, CacheJsonContext.Default.ThumbnailIndexFile),
                cancellationToken).ConfigureAwait(false);

            _cache.Commit(fingerprint, CacheArtifacts.Thumbnails, temp, CacheArtifacts.ThumbnailsFolder);
            var final = new ThumbnailSheet(_cache.PathOf(fingerprint, CacheArtifacts.ThumbnailsFolder), interval, count);
            _known[fingerprint] = final;
            return final;
        }
        finally
        {
            TryDeleteDirectory(temp);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The startup cleanup removes abandoned temps.
        }
    }

    private ThumbnailSheet? Load(string fingerprint)
    {
        var folder = _cache.TryGetArtifact(fingerprint, CacheArtifacts.Thumbnails, CacheArtifacts.ThumbnailsFolder);
        if (folder is null)
        {
            return null;
        }

        try
        {
            var json = File.ReadAllBytes(Path.Combine(folder, CacheArtifacts.ThumbnailsIndexFile));
            var file = JsonSerializer.Deserialize(json, CacheJsonContext.Default.ThumbnailIndexFile);
            if (file is null || file.Count <= 0 || file.IntervalSec <= 0 || file.TileW != ThumbnailSheet.TileWidth
                || file.TileH != ThumbnailSheet.TileHeight || file.Cols != ThumbnailSheet.Columns || file.Rows != ThumbnailSheet.Rows)
            {
                return null;
            }

            var sheet = new ThumbnailSheet(folder, (long)Math.Round(file.IntervalSec * 1e6), file.Count);
            return sheet.SheetPaths.All(File.Exists) ? sheet : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
