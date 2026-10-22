using System.Collections.Concurrent;
using System.Text;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Parsing;

namespace Cutlass.Media.Services;

/// <summary>
/// Probes media files with ffprobe (TECH_BRIEF 6.3) and keeps the results in memory, keyed by path, size and
/// modification time, so a changed file is probed again. With a <see cref="MediaCache"/> and a
/// <see cref="Fingerprinter"/> the raw output is also stored as the media folder's <c>probe.json</c> artifact
/// (TECH_BRIEF 6.1), keyed by content: a re-import or a reopened project whose file still has the memoised size and
/// modification time reads it back instead of running ffprobe. Thread-safe.
/// </summary>
public sealed class ProbeService
{
    /// <summary>How long one ffprobe run may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly WorkDirectory _work;
    private readonly ConcurrentDictionary<string, (long Size, long MtimeTicks, ProbeResult Result)> _cache;
    private readonly MediaCache? _mediaCache;
    private readonly Fingerprinter? _fingerprints;
    private int _ffprobeRuns;

    /// <summary>Creates the service with an in-memory memo only.</summary>
    /// <param name="runner">Runs ffprobe.</param>
    /// <param name="toolset">The validated tools.</param>
    /// <param name="work">The work directory ffprobe runs in.</param>
    public ProbeService(ProcessRunner runner, FfmpegToolset toolset, WorkDirectory work)
        : this(runner, toolset, work, null, null)
    {
    }

    /// <summary>Creates the service.</summary>
    /// <param name="runner">Runs ffprobe.</param>
    /// <param name="toolset">The validated tools.</param>
    /// <param name="work">The work directory ffprobe runs in.</param>
    /// <param name="mediaCache">Stores <c>probe.json</c> per media, or null.</param>
    /// <param name="fingerprints">The fingerprint memo that keys the media cache, or null (no disk cache then).</param>
    public ProbeService(ProcessRunner runner, FfmpegToolset toolset, WorkDirectory work, MediaCache? mediaCache, Fingerprinter? fingerprints)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(work);
        _runner = runner;
        _toolset = toolset;
        _work = work;
        _mediaCache = mediaCache;
        _fingerprints = fingerprints;
        _cache = new ConcurrentDictionary<string, (long, long, ProbeResult)>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    /// <summary>How many times this instance ran ffprobe (memo and <c>probe.json</c> hits do not count).</summary>
    public int FfprobeRuns => Volatile.Read(ref _ffprobeRuns);

    /// <summary>The ffprobe arguments for a file: <c>-v error -print_format json -show_format -show_streams</c>.</summary>
    /// <param name="file">The absolute path (passed as one argument, never quoted or escaped).</param>
    /// <returns>The arguments, without the executable.</returns>
    public static IReadOnlyList<string> BuildArguments(string file) =>
        ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", file];

    /// <summary>Probes a file, or returns the cached result when its size and modification time are unchanged.</summary>
    /// <param name="path">The media file.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The probe result.</returns>
    /// <exception cref="FileNotFoundException">When the file does not exist.</exception>
    /// <exception cref="FfmpegException">When ffprobe fails, times out or prints something that is not ffprobe JSON.</exception>
    public async Task<ProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The media file does not exist.", full);
        }

        var size = info.Length;
        var mtime = info.LastWriteTimeUtc.Ticks;
        if (_cache.TryGetValue(full, out var hit) && hit.Size == size && hit.MtimeTicks == mtime)
        {
            return hit.Result;
        }

        // TECH_BRIEF 6.5: probe.json in the media cache, keyed by the (memoised) content fingerprint.
        // Files inside the cache (proxies and their temps) are not media: they get no cache entry of their own.
        string? fingerprint = null;
        if (_mediaCache is { } mediaCache && _fingerprints is { } fingerprints && !IsInside(full, mediaCache.CacheRoot))
        {
            fingerprint = await fingerprints.GetAsync(full, cancellationToken).ConfigureAwait(false);
            if (await Task.Run(() => ReadStored(mediaCache, fingerprint), cancellationToken).ConfigureAwait(false) is { } stored)
            {
                _cache[full] = (size, mtime, stored);
                return stored;
            }
        }

        _work.EnsureCreated();
        var spec = new ProcessSpec(_toolset.FfprobePath, BuildArguments(full), _work.Path, false, ProcessPurpose.Probe, false);
        Interlocked.Increment(ref _ffprobeRuns);
        var run = await _runner.RunAsync(spec, Timeout, cancellationToken).ConfigureAwait(false);
        run.EnsureSuccess();

        var json = run.StdoutText;
        ProbeResult result;
        try
        {
            result = new ProbeResult(ProbeParser.Parse(json), json);
        }
        catch (FormatException ex)
        {
            throw new FfmpegException(
                "ffprobe printed output Cutlass cannot read.",
                run.ExitCode,
                run.Tail(ProcessResult.ExceptionTailLines),
                run.DisplayCommand,
                ex);
        }

        _cache[full] = (size, mtime, result);
        if (_mediaCache is { } cacheToWrite && fingerprint is not null)
        {
            Store(cacheToWrite, fingerprint, json);
        }

        return result;
    }

    private static bool IsInside(string path, string folder)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    private static ProbeResult? ReadStored(MediaCache cache, string fingerprint)
    {
        if (cache.TryGetArtifact(fingerprint, CacheArtifacts.Probe, CacheArtifacts.ProbeFile) is not { } path)
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return new ProbeResult(ProbeParser.Parse(json), json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            // Damaged or unreadable: probe again and overwrite it.
            return null;
        }
    }

    private static void Store(MediaCache cache, string fingerprint, string json)
    {
        string? temp = null;
        try
        {
            temp = cache.NewTempFile(fingerprint, "probe", ".json");
            File.WriteAllText(temp, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            cache.Commit(fingerprint, CacheArtifacts.Probe, temp, CacheArtifacts.ProbeFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The disk copy only saves a later ffprobe run.
        }
        finally
        {
            if (temp is not null)
            {
                try
                {
                    File.Delete(temp);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Removed by the startup cleanup.
                }
            }
        }
    }

    /// <summary>Forgets the cached result for a file (after relink or a reported change).</summary>
    /// <param name="path">The media file.</param>
    public void Invalidate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _cache.TryRemove(Path.GetFullPath(path), out _);
    }
}
