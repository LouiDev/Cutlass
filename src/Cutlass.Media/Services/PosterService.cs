using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Services;

/// <summary>
/// Renders the media bin poster of an asset (TECH_BRIEF 6.5): one frame at 10% of the duration, scaled to fit
/// 320x180. With a <see cref="MediaCache"/> the poster is the media folder's <c>poster.jpg</c> artifact (TECH_BRIEF
/// 6.1); a poster left at the M4 location <c>&lt;cache&gt;/posters/&lt;fingerprint hex&gt;.jpg</c> is still read and
/// moved into the media cache on first use. Without a cache (tests) the M4 location is used. Posters are keyed by
/// content, so a renamed file keeps its poster. Every write is temp-then-rename. Thread-safe.
/// </summary>
public sealed class PosterService
{
    /// <summary>The poster box width.</summary>
    public const int MaxWidth = 320;

    /// <summary>The poster box height.</summary>
    public const int MaxHeight = 180;

    /// <summary>How long one poster render may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly AppPaths _paths;
    private readonly MediaCache? _cache;

    /// <summary>Creates the service without a media cache (posters in <see cref="PosterDirectory"/>).</summary>
    /// <param name="runner">Runs ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="paths">The app paths (posters live under the cache root).</param>
    public PosterService(ProcessRunner runner, FfmpegToolset toolset, AppPaths paths)
        : this(runner, toolset, paths, null)
    {
    }

    /// <summary>Creates the service.</summary>
    /// <param name="runner">Runs ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="paths">The app paths (the M4 poster folder lives under the cache root).</param>
    /// <param name="cache">The media cache that holds the <c>poster</c> artifact, or null for the M4 folder.</param>
    public PosterService(ProcessRunner runner, FfmpegToolset toolset, AppPaths paths, MediaCache? cache)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(paths);
        _runner = runner;
        _toolset = toolset;
        _paths = paths;
        _cache = cache;
    }

    /// <summary>The M4 poster folder, <c>&lt;cache&gt;/posters</c> (read as a fallback when a media cache is used).</summary>
    public string PosterDirectory => Path.Combine(_paths.CacheRoot, "posters");

    /// <summary>
    /// The poster arguments (TECH_BRIEF 6.5):
    /// <c>-hide_banner -nostdin -v error [-ss S] -i file -map 0:v -frames:v 1
    /// -vf scale=w=320:h=180:force_original_aspect_ratio=decrease -q:v 4 -y output</c>.
    /// </summary>
    /// <param name="file">The media file (one argument, never quoted).</param>
    /// <param name="videoStreamIndex">The absolute video stream index.</param>
    /// <param name="seekMicros">The input seek in microseconds, or null for none (stills).</param>
    /// <param name="output">The output path.</param>
    /// <returns>The arguments, without the executable.</returns>
    public static IReadOnlyList<string> BuildArguments(string file, int videoStreamIndex, long? seekMicros, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        List<string> args = ["-hide_banner", "-nostdin", "-v", "error"];
        if (seekMicros is { } seek)
        {
            args.Add("-ss");
            args.Add(FfNum.Seconds(seek));
        }

        args.AddRange(
        [
            "-i", file, "-map", "0:" + FfNum.Integer(videoStreamIndex), "-frames:v", "1",
            "-vf", $"scale=w={MaxWidth}:h={MaxHeight}:force_original_aspect_ratio=decrease", "-q:v", "4", "-y", output,
        ]);
        return args;
    }

    /// <summary>
    /// The file name stem of a fingerprint: the hex part of <c>sha256:&lt;hex&gt;</c>, because a colon is not
    /// allowed in Windows file names.
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>A bare file name stem.</returns>
    public static string FileStem(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var colon = fingerprint.IndexOf(':', StringComparison.Ordinal);
        var stem = colon >= 0 ? fingerprint[(colon + 1)..] : fingerprint;
        return WorkDirectory.IsBareName(stem) ? stem : WorkDirectory.ShortHash(System.Text.Encoding.UTF8.GetBytes(fingerprint), 64);
    }

    /// <summary>The M4 poster path of an asset in <see cref="PosterDirectory"/> (whether or not it exists).</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The full path.</returns>
    public string PathFor(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return Path.Combine(PosterDirectory, FileStem(asset.Fingerprint) + ".jpg");
    }

    /// <summary>Returns the poster of an asset, rendering it first when it does not exist.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="ct">Cancels the render.</param>
    /// <returns>The poster path, or null for media without video (audio-only).</returns>
    /// <exception cref="FfmpegException">When ffmpeg cannot produce a frame.</exception>
    public async Task<string?> GetOrCreateAsync(MediaAsset asset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Info.Video is null)
        {
            return null;
        }

        if (_cache is not { } cache)
        {
            return await GetOrCreateInFolderAsync(asset, ct).ConfigureAwait(false);
        }

        var fingerprint = asset.Fingerprint;
        if (await Task.Run(() => cache.TryGetArtifact(fingerprint, CacheArtifacts.Poster, CacheArtifacts.PosterFile), ct).ConfigureAwait(false) is { } cached)
        {
            return cached;
        }

        var legacy = PathFor(asset);
        if (HasContent(legacy))
        {
            return await Task.Run(() => MigrateLegacy(cache, fingerprint, legacy), ct).ConfigureAwait(false);
        }

        Directory.CreateDirectory(_paths.WorkDirectory);
        var temp = cache.NewTempFile(fingerprint, "poster", ".jpg");
        try
        {
            await RenderPosterAsync(asset, temp, ct).ConfigureAwait(false);
            cache.Commit(fingerprint, CacheArtifacts.Poster, temp, CacheArtifacts.PosterFile);
            return cache.PathOf(fingerprint, CacheArtifacts.PosterFile);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    // M5 tidy: a poster from the M4 folder moves into the media cache the first time it is asked for (lazy migration).
    private static string MigrateLegacy(MediaCache cache, string fingerprint, string legacy)
    {
        var temp = cache.NewTempFile(fingerprint, "poster", ".jpg");
        try
        {
            File.Copy(legacy, temp, overwrite: true);
            cache.Commit(fingerprint, CacheArtifacts.Poster, temp, CacheArtifacts.PosterFile);
            TryDelete(legacy);
            return cache.PathOf(fingerprint, CacheArtifacts.PosterFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The old file still works; the next request tries the move again.
            return legacy;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task<string> GetOrCreateInFolderAsync(MediaAsset asset, CancellationToken ct)
    {
        var final = PathFor(asset);
        if (HasContent(final))
        {
            return final;
        }

        Directory.CreateDirectory(PosterDirectory);
        Directory.CreateDirectory(_paths.WorkDirectory);
        var temp = Path.Combine(PosterDirectory, FileStem(asset.Fingerprint) + ".tmp-" + Guid.NewGuid().ToString("N")[..8] + ".jpg");
        try
        {
            await RenderPosterAsync(asset, temp, ct).ConfigureAwait(false);
            File.Move(temp, final, overwrite: true);
            return final;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private async Task RenderPosterAsync(MediaAsset asset, string temp, CancellationToken ct)
    {
        var video = asset.Info.Video!;
        long? seek = asset.Info.IsStill || asset.Info.DurationMicros <= 0 ? null : asset.Info.DurationMicros / 10;
        var result = await RenderAsync(asset.Path, video.StreamIndex, seek, temp, ct).ConfigureAwait(false);
        if (seek is not null && !HasContent(temp))
        {
            // A reported duration beyond the last frame yields no frame at the seek point; take the first frame.
            result = await RenderAsync(asset.Path, video.StreamIndex, null, temp, ct).ConfigureAwait(false);
        }

        if (!HasContent(temp))
        {
            result.EnsureSuccess();
            throw new FfmpegException(
                "ffmpeg produced no poster frame.",
                result.ExitCode,
                result.Tail(ProcessResult.ExceptionTailLines),
                result.DisplayCommand);
        }
    }

    private static bool HasContent(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

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
            // The startup cleanup removes abandoned temp files later.
        }
    }

    private Task<ProcessResult> RenderAsync(string file, int stream, long? seek, string output, CancellationToken ct)
    {
        var spec = new ProcessSpec(
            _toolset.FfmpegPath,
            BuildArguments(file, stream, seek, output),
            _paths.WorkDirectory,
            RedirectStdin: false,
            ProcessPurpose.Background,
            BelowNormalPriority: true);
        return _runner.RunAsync(spec, Timeout, ct);
    }
}
