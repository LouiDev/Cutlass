using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Parsing;

namespace Cutlass.Media.Services;

/// <summary>
/// Builds frame indexes with an ffprobe packet pass (TECH_BRIEF 6.4):
/// <c>ffprobe -v error -select_streams &lt;v&gt; -show_entries packet=pts,dts,flags -of csv=p=0 &lt;file&gt;</c>.
/// It only demuxes, so it is fast. Results are kept in memory, keyed by path, size, modification time and stream.
/// With a <see cref="MediaCache"/>, <see cref="GetAsync(MediaAsset, CancellationToken)"/> also keeps the index as
/// <c>frames.idx</c> in the media's cache folder (TECH_BRIEF 6.1 "CTLX" format, see <see cref="WriteIndex"/>), so a
/// reopened project needs no packet pass. Concurrent requests for the same stream share one pass. Thread-safe.
/// </summary>
public sealed class FrameIndexService
{
    /// <summary>The magic of <c>frames.idx</c> files.</summary>
    public const string IndexMagic = "CTLX";

    /// <summary>The current <c>frames.idx</c> format version.</summary>
    public const ushort IndexFormatVersion = 1;

    /// <summary>How long one packet pass may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private const int IndexHeaderBytes = 26;

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly WorkDirectory _work;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<FrameIndex?>>> _inFlight;
    private int _packetPasses;

    /// <summary>Creates the service without a disk cache.</summary>
    /// <param name="runner">Runs ffprobe.</param>
    /// <param name="toolset">The validated tools.</param>
    /// <param name="work">The work directory ffprobe runs in.</param>
    public FrameIndexService(ProcessRunner runner, FfmpegToolset toolset, WorkDirectory work)
        : this(runner, toolset, work, cache: null)
    {
    }

    /// <summary>Creates the service.</summary>
    /// <param name="runner">Runs ffprobe.</param>
    /// <param name="toolset">The validated tools.</param>
    /// <param name="work">The work directory ffprobe runs in.</param>
    /// <param name="cache">The media cache for <c>frames.idx</c>, or null to keep indexes in memory only.</param>
    public FrameIndexService(ProcessRunner runner, FfmpegToolset toolset, WorkDirectory work, MediaCache? cache)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(work);
        _runner = runner;
        _toolset = toolset;
        _work = work;
        Cache = cache;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _cache = new ConcurrentDictionary<string, CacheEntry>(comparer);
        _inFlight = new ConcurrentDictionary<string, Lazy<Task<FrameIndex?>>>(comparer);
    }

    /// <summary>The number of files with a cached result (an index or a known CFR fallback).</summary>
    public int CachedCount => _cache.Count;

    /// <summary>The media cache holding <c>frames.idx</c> files, or null.</summary>
    public MediaCache? Cache { get; }

    /// <summary>How many packet passes this instance has run (tests use it to prove disk hits).</summary>
    public int PacketPassCount => Volatile.Read(ref _packetPasses);

    /// <summary>
    /// Writes a frame index in the <c>frames.idx</c> format (TECH_BRIEF 6.1), little-endian:
    /// <c>"CTLX" u16 ver, i64 tbNum, i64 tbDen, i32 count, i64 pts[count]</c> ascending.
    /// </summary>
    /// <remarks>A count of 0 records "no usable index" (the CFR fallback), so that result is cached too.</remarks>
    /// <param name="stream">The destination.</param>
    /// <param name="timeBase">The stream time base.</param>
    /// <param name="index">The index, or null for "no usable index".</param>
    public static void WriteIndex(Stream stream, Rational timeBase, FrameIndex? index)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var count = index?.Count ?? 0;
        var buffer = new byte[IndexHeaderBytes + (8L * count)];
        System.Text.Encoding.ASCII.GetBytes(IndexMagic, buffer);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), IndexFormatVersion);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(6), timeBase.Num);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(14), timeBase.Den);
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(22), count);
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(IndexHeaderBytes + (8 * i)), index![i]);
        }

        stream.Write(buffer);
    }

    /// <summary>Reads a frame index written by <see cref="WriteIndex"/>.</summary>
    /// <param name="stream">The source.</param>
    /// <param name="timeBase">The recorded time base.</param>
    /// <param name="index">The index, or null when the data records "no usable index".</param>
    /// <returns>False when the data is not a valid <c>frames.idx</c> of the current version.</returns>
    public static bool TryReadIndex(Stream stream, out Rational timeBase, out FrameIndex? index)
    {
        ArgumentNullException.ThrowIfNull(stream);
        timeBase = default;
        index = null;
        var header = new byte[IndexHeaderBytes];
        if (!TryReadExactly(stream, header)
            || !header.AsSpan(0, 4).SequenceEqual("CTLX"u8)
            || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(4)) != IndexFormatVersion)
        {
            return false;
        }

        var num = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(6));
        var den = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(14));
        var count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(22));
        if (num <= 0 || den <= 0 || count < 0 || (stream.CanSeek && stream.Length - stream.Position < 8L * count))
        {
            return false;
        }

        var bytes = new byte[8L * count];
        if (!TryReadExactly(stream, bytes))
        {
            return false;
        }

        var pts = new long[count];
        for (var i = 0; i < count; i++)
        {
            pts[i] = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(8 * i));
        }

        timeBase = new Rational(num, den);
        try
        {
            index = count == 0 ? null : new FrameIndex(timeBase, pts);
        }
        catch (ArgumentException)
        {
            // Not strictly ascending: corrupt.
            return false;
        }

        return true;
    }

    /// <summary>Reads a <c>frames.idx</c> file from disk.</summary>
    /// <param name="path">The file.</param>
    /// <param name="timeBase">The recorded time base.</param>
    /// <param name="index">The index, or null for "no usable index".</param>
    /// <returns>False when the file is missing, unreadable or invalid.</returns>
    public static bool TryReadIndexFile(string path, out Rational timeBase, out FrameIndex? index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        timeBase = default;
        index = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            return TryReadIndex(stream, out timeBase, out index);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Writes a <c>frames.idx</c> file (not atomic: write a temp file and rename it).</summary>
    /// <param name="path">The file.</param>
    /// <param name="timeBase">The stream time base.</param>
    /// <param name="index">The index, or null for "no usable index".</param>
    public static void WriteIndexFile(string path, Rational timeBase, FrameIndex? index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        WriteIndex(stream, timeBase, index);
        stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Builds an index from the packet listing: drops <c>D</c> packets, uses <c>dts</c> when <c>pts</c> is
    /// <c>N/A</c>, sorts and removes duplicates.
    /// </summary>
    /// <param name="csv">The ffprobe output.</param>
    /// <param name="timeBase">The stream time base.</param>
    /// <returns>The index, or null when more than 5% of the lines were unusable (CFR fallback, "VFR-unsafe").</returns>
    public static FrameIndex? Build(string csv, Rational timeBase)
    {
        if (!timeBase.IsPositive)
        {
            return null;
        }

        var pts = PacketIndexParser.Parse(csv);
        return pts is null ? null : new FrameIndex(timeBase, pts);
    }

    /// <summary>Returns the index of an asset's video stream.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="cancellationToken">Cancels the packet pass.</param>
    /// <returns>The index, or null for stills, audio-only media and files without a usable index.</returns>
    /// <exception cref="FfmpegException">When ffprobe fails.</exception>
    /// <remarks>
    /// With a <see cref="Cache"/>, a current <c>frames.idx</c> of the asset's fingerprint answers first, and a new
    /// index is written there. The disk cache is used only while the file still has the size and modification time
    /// recorded in the asset, because the fingerprint describes the content at import.
    /// </remarks>
    public async Task<FrameIndex?> GetAsync(MediaAsset asset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Info.IsStill || asset.Info.Video is not { } video)
        {
            return null;
        }

        if (Cache is not { } cache || string.IsNullOrWhiteSpace(asset.Fingerprint) || !MatchesFile(asset))
        {
            return await GetAsync(asset.Path, video.StreamIndex, video.TimeBase, cancellationToken).ConfigureAwait(false);
        }

        var full = Path.GetFullPath(asset.Path);
        var key = Key(full, video.StreamIndex);
        var mtime = UtcTicks(asset.MtimeUtc);
        if (TryMemory(key, asset.SizeBytes, mtime, video.TimeBase, out var known))
        {
            return known;
        }

        var stored = await Task.Run(() => cache.TryGetArtifact(asset.Fingerprint, CacheArtifacts.Index, CacheArtifacts.IndexFile), cancellationToken)
            .ConfigureAwait(false);
        if (stored is not null && TryReadIndexFile(stored, out var storedTimeBase, out var storedIndex) && storedTimeBase == video.TimeBase)
        {
            _cache[key] = new CacheEntry(asset.SizeBytes, mtime, video.TimeBase, storedIndex);
            return storedIndex;
        }

        var index = await GetAsync(full, video.StreamIndex, video.TimeBase, cancellationToken).ConfigureAwait(false);
        try
        {
            var temp = cache.NewTempFile(asset.Fingerprint, "frames", ".idx");
            WriteIndexFile(temp, video.TimeBase, index);
            cache.Commit(asset.Fingerprint, CacheArtifacts.Index, temp, CacheArtifacts.IndexFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The disk cache is an optimisation; the index is still in memory.
        }

        return index;
    }

    /// <summary>Returns the index of one video stream of a file, from memory when the file is unchanged.</summary>
    /// <param name="path">The media file (any path; passed as one argument).</param>
    /// <param name="videoStreamIndex">The absolute video stream index.</param>
    /// <param name="timeBase">The stream time base from the probe.</param>
    /// <param name="cancellationToken">Cancels the packet pass.</param>
    /// <returns>The index, or null when there is no usable index.</returns>
    /// <exception cref="FileNotFoundException">When the file does not exist.</exception>
    /// <exception cref="FfmpegException">When ffprobe fails or times out.</exception>
    public async Task<FrameIndex?> GetAsync(string path, int videoStreamIndex, Rational timeBase, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(videoStreamIndex);
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The media file does not exist.", full);
        }

        var key = Key(full, videoStreamIndex);
        var size = info.Length;
        var mtime = info.LastWriteTimeUtc.Ticks;
        if (TryMemory(key, size, mtime, timeBase, out var known))
        {
            return known;
        }

        // Concurrent requests (the catalog's background pass and a scheduler job) share one packet pass. The shared
        // pass runs without the callers' tokens, so one caller's cancellation never fails another; a cancelled caller
        // stops waiting and the pass (demux only, with a timeout) finishes on its own.
        var flightKey = string.Create(CultureInfo.InvariantCulture, $"{key}|{size}|{mtime}|{timeBase.Num}/{timeBase.Den}");
        var flight = _inFlight.GetOrAdd(
            flightKey,
            _ => new Lazy<Task<FrameIndex?>>(() => RunPassAsync(full, videoStreamIndex, timeBase, key, size, mtime, flightKey)));
        return await flight.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forgets every cached index of a file (after relink or a reported change).</summary>
    /// <param name="path">The media file.</param>
    public void Invalidate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var prefix = Path.GetFullPath(path) + "|";
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var key in _cache.Keys)
        {
            if (key.StartsWith(prefix, comparison))
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private static string Key(string fullPath, int videoStreamIndex) =>
        fullPath + "|" + videoStreamIndex.ToString(CultureInfo.InvariantCulture);

    // Like the project serializer: an unspecified kind already is UTC.
    private static long UtcTicks(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime().Ticks : value.Ticks;

    private static bool MatchesFile(MediaAsset asset)
    {
        try
        {
            var info = new FileInfo(asset.Path);
            return info.Exists && info.Length == asset.SizeBytes && info.LastWriteTimeUtc.Ticks == UtcTicks(asset.MtimeUtc);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryReadExactly(Stream stream, byte[] buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    private bool TryMemory(string key, long size, long mtimeTicks, Rational timeBase, out FrameIndex? index)
    {
        if (_cache.TryGetValue(key, out var hit) && hit.Size == size && hit.MtimeTicks == mtimeTicks && hit.TimeBase == timeBase)
        {
            index = hit.Index;
            return true;
        }

        index = null;
        return false;
    }

    private async Task<FrameIndex?> RunPassAsync(string full, int videoStreamIndex, Rational timeBase, string key, long size, long mtime, string flightKey)
    {
        try
        {
            return await RunPassCoreAsync(full, videoStreamIndex, timeBase, key, size, mtime).ConfigureAwait(false);
        }
        finally
        {
            // A finished pass leaves its result in memory; a failed one must not answer later requests.
            _inFlight.TryRemove(flightKey, out _);
        }
    }

    private async Task<FrameIndex?> RunPassCoreAsync(string full, int videoStreamIndex, Rational timeBase, string key, long size, long mtime)
    {
        _work.EnsureCreated();
        var spec = new ProcessSpec(
            _toolset.FfprobePath,
            PacketIndexParser.BuildArguments(videoStreamIndex, full),
            _work.Path,
            RedirectStdin: false,
            ProcessPurpose.Background,
            BelowNormalPriority: true);
        Interlocked.Increment(ref _packetPasses);
        var run = await _runner.RunAsync(spec, Timeout, CancellationToken.None).ConfigureAwait(false);
        run.EnsureSuccess();

        var index = Build(run.StdoutText, timeBase);
        _cache[key] = new CacheEntry(size, mtime, timeBase, index);
        return index;
    }

    private sealed record CacheEntry(long Size, long MtimeTicks, Rational TimeBase, FrameIndex? Index);
}
