using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Cache;

/// <summary>
/// The per-media artifact cache (TECH_BRIEF 6.1): <c>&lt;cache&gt;/media/&lt;fp[0..2]&gt;/&lt;fp&gt;/</c> with a
/// <c>manifest.json</c> that records each artifact's version, the proxy mapping and the last access time. Artifacts
/// are written as <c>name.tmp-&lt;guid8&gt;.ext</c> (a folder for thumbnails), renamed into place and only then
/// recorded in the manifest, so a crash never leaves a half-written artifact that counts as present. An LRU on
/// <c>lastAccessUtc</c> trims the cache to its limit and never evicts protected (open project) media.
/// </summary>
/// <remarks>
/// <para>Thread-safe. Manifest changes are serialised inside the process; several app instances may share the folder
/// because every write is temp-then-rename and a rename that loses a race keeps the other instance's equal artifact.</para>
/// <para>Every member does file I/O, so call them off the UI thread. <see cref="UsedBytes"/> is a cached value
/// refreshed by <see cref="Measure"/>, commits, removals and eviction.</para>
/// </remarks>
public sealed class MediaCache
{
    /// <summary>Bytes per GB as the settings and status bar count them (1024^3).</summary>
    public const long BytesPerGb = 1024L * 1024 * 1024;

    /// <summary>The default size limit, 20 GB (DESIGN 10).</summary>
    public const long DefaultLimitBytes = 20 * BytesPerGb;

    /// <summary>The manifest file name.</summary>
    public const string ManifestFile = "manifest.json";

    /// <summary>The marker in every temp artifact name.</summary>
    public const string TempMarker = ".tmp-";

    /// <summary>How often one entry's <c>lastAccessUtc</c> is rewritten at most (the LRU needs no finer grain).</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(1);

    private const int MoveAttempts = 3;

    private readonly object _gate = new();
    private readonly Dictionary<string, long> _sizes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _touched = new(StringComparer.Ordinal);
    private readonly Func<DateTime> _utcNow;
    private long _limitBytes;

    /// <summary>Creates the cache object. No I/O happens until a member is used.</summary>
    /// <param name="cacheRoot">The versioned cache root (<see cref="AppPaths.CacheRoot"/>); media lives in <c>media/</c> below it.</param>
    /// <param name="ffmpegBuild">The ffmpeg build id recorded in manifests, or null.</param>
    /// <param name="limitBytes">The size limit.</param>
    /// <param name="utcNow">The clock (tests), or null for <see cref="DateTime.UtcNow"/>.</param>
    public MediaCache(string cacheRoot, string? ffmpegBuild = null, long limitBytes = DefaultLimitBytes, Func<DateTime>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        ArgumentOutOfRangeException.ThrowIfNegative(limitBytes);
        CacheRoot = AppPaths.Normalize(cacheRoot);
        Root = Path.Combine(CacheRoot, "media");
        FfmpegBuild = ffmpegBuild;
        _limitBytes = limitBytes;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Raised on the calling worker thread whenever <see cref="UsedBytes"/> may have changed.</summary>
    public event Action? UsageChanged;

    /// <summary>The versioned cache root.</summary>
    public string CacheRoot { get; }

    /// <summary>The media folder, <c>&lt;cache&gt;/media</c>.</summary>
    public string Root { get; }

    /// <summary>The ffmpeg build id recorded in manifests (diagnostics only; a new build invalidates nothing).</summary>
    public string? FfmpegBuild { get; }

    /// <summary>The size limit the LRU trims to (settings: cache size in GB).</summary>
    public long LimitBytes
    {
        get => Interlocked.Read(ref _limitBytes);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            Interlocked.Exchange(ref _limitBytes, value);
        }
    }

    /// <summary>The bytes used by every measured entry (see the class remarks).</summary>
    public long UsedBytes
    {
        get
        {
            lock (_gate)
            {
                return _sizes.Values.Sum();
            }
        }
    }

    /// <summary>
    /// The folder name of a fingerprint: the hex part of <c>sha256:&lt;hex&gt;</c>, or a SHA-256 of the whole text
    /// when that part is not plain lowercase hex.
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>The key (at least 2 characters).</returns>
    public static string KeyOf(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        var colon = fingerprint.IndexOf(':', StringComparison.Ordinal);
        var hex = colon >= 0 ? fingerprint[(colon + 1)..] : fingerprint;
        return hex.Length >= 2 && hex.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            ? hex
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
    }

    /// <summary>
    /// Picks the entries the LRU deletes: the least recently used first, skipping protected ones, until the total
    /// is at or below the limit. Pure.
    /// </summary>
    /// <param name="entries">Every entry with its size and last access.</param>
    /// <param name="limitBytes">The limit.</param>
    /// <param name="protectedKeys">Keys that are never evicted.</param>
    /// <returns>The entries to delete, oldest first.</returns>
    public static IReadOnlyList<CacheEntryUsage> SelectEvictions(
        IReadOnlyList<CacheEntryUsage> entries,
        long limitBytes,
        IReadOnlySet<string> protectedKeys)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(protectedKeys);
        var total = entries.Sum(e => e.Bytes);
        var evict = new List<CacheEntryUsage>();
        foreach (var entry in entries.OrderBy(e => e.LastAccessUtc).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            if (total <= limitBytes)
            {
                break;
            }

            if (protectedKeys.Contains(entry.Key))
            {
                continue;
            }

            evict.Add(entry);
            total -= entry.Bytes;
        }

        return evict;
    }

    /// <summary>The folder of a media item (whether or not it exists).</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>The full path.</returns>
    public string DirectoryFor(string fingerprint)
    {
        var key = KeyOf(fingerprint);
        return Path.Combine(Root, key[..2], key);
    }

    /// <summary>The full path of a file inside a media item's folder.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="relativePath">The file name (or a path below the folder, such as <c>thumbs/sheet_001.jpg</c>).</param>
    /// <returns>The full path.</returns>
    public string PathOf(string fingerprint, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Path.Combine(DirectoryFor(fingerprint), relativePath);
    }

    /// <summary>Reads a manifest.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>The manifest, or null when missing or unreadable.</returns>
    public CacheManifest? ReadManifest(string fingerprint)
    {
        var path = PathOf(fingerprint, ManifestFile);
        lock (_gate)
        {
            return ReadManifestFile(path);
        }
    }

    /// <summary>True when the manifest records the artifact at its current version and its file or folder exists.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="artifact">The artifact name (<see cref="CacheArtifacts"/>).</param>
    /// <param name="relativePath">The file or folder that must exist.</param>
    /// <returns>True when present.</returns>
    public bool HasArtifact(string fingerprint, string artifact, string relativePath)
    {
        var manifest = ReadManifest(fingerprint);
        var path = PathOf(fingerprint, relativePath);
        return manifest is not null && manifest.HasCurrent(artifact) && (File.Exists(path) || Directory.Exists(path));
    }

    /// <summary>
    /// Returns the path of a current artifact and records the access for the LRU (at most once per
    /// <see cref="TouchInterval"/>).
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="artifact">The artifact name.</param>
    /// <param name="relativePath">The artifact's file or folder.</param>
    /// <returns>The full path, or null when the artifact is missing or out of date.</returns>
    public string? TryGetArtifact(string fingerprint, string artifact, string relativePath)
    {
        if (!HasArtifact(fingerprint, artifact, relativePath))
        {
            return null;
        }

        Touch(fingerprint);
        return PathOf(fingerprint, relativePath);
    }

    /// <summary>
    /// Creates the media folder and returns a fresh temp file path <c>&lt;stem&gt;.tmp-&lt;guid8&gt;&lt;extension&gt;</c>
    /// in it. The file itself is not created.
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="stem">The artifact's file stem, for example <c>frames</c>.</param>
    /// <param name="extension">The extension with its dot, for example <c>.idx</c>.</param>
    /// <returns>The full temp path.</returns>
    public string NewTempFile(string fingerprint, string stem, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        ArgumentNullException.ThrowIfNull(extension);
        var directory = DirectoryFor(fingerprint);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, stem + TempMarker + Guid.NewGuid().ToString("N")[..8] + extension);
    }

    /// <summary>Creates and returns a fresh temp folder <c>&lt;stem&gt;.tmp-&lt;guid8&gt;</c> in the media folder.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="stem">The artifact's folder name, for example <c>thumbs</c>.</param>
    /// <returns>The full path of the created folder.</returns>
    public string NewTempDirectory(string fingerprint, string stem)
    {
        var path = NewTempFile(fingerprint, stem, string.Empty);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Renames one temp file or folder into place and records the artifact. See the other overload.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="artifact">The artifact name.</param>
    /// <param name="tempPath">The temp file or folder.</param>
    /// <param name="finalName">The final name inside the media folder.</param>
    public void Commit(string fingerprint, string artifact, string tempPath, string finalName) =>
        Commit(fingerprint, artifact, [(tempPath, finalName)], update: null);

    /// <summary>
    /// Renames temp files or folders into place, then records <paramref name="artifact"/> at its current version
    /// (plus anything <paramref name="update"/> changes) in the manifest. When a rename loses a race against another
    /// instance whose artifact is in place, the temp is deleted and the existing one kept (same content and version).
    /// </summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="artifact">The artifact name.</param>
    /// <param name="moves">Each temp path with its final name inside the media folder.</param>
    /// <param name="update">Further manifest changes (the proxy mapping), or null.</param>
    /// <exception cref="IOException">When a rename fails and nothing is in place.</exception>
    public void Commit(
        string fingerprint,
        string artifact,
        IReadOnlyList<(string TempPath, string FinalName)> moves,
        Func<CacheManifest, CacheManifest>? update)
    {
        ArgumentNullException.ThrowIfNull(moves);
        _ = CacheArtifacts.VersionOf(artifact);
        var directory = DirectoryFor(fingerprint);
        Directory.CreateDirectory(directory);
        foreach (var (temp, finalName) in moves)
        {
            MoveIntoPlace(temp, Path.Combine(directory, finalName));
        }

        UpdateManifest(fingerprint, m => (update?.Invoke(m) ?? m).WithArtifact(artifact));
        MeasureEntry(KeyOf(fingerprint));
    }

    /// <summary>Changes a manifest atomically (creating it when missing) and stamps the access time.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="update">The change.</param>
    /// <returns>The written manifest.</returns>
    public CacheManifest UpdateManifest(string fingerprint, Func<CacheManifest, CacheManifest> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        var key = KeyOf(fingerprint);
        var directory = DirectoryFor(fingerprint);
        var path = Path.Combine(directory, ManifestFile);
        lock (_gate)
        {
            Directory.CreateDirectory(directory);
            var current = ReadManifestFile(path) ?? new CacheManifest { Fingerprint = fingerprint };
            var now = _utcNow();
            var next = update(current) with { Fingerprint = fingerprint, FfmpegBuild = FfmpegBuild ?? current.FfmpegBuild, LastAccessUtc = now };
            WriteManifestFile(path, next);
            _touched[key] = now;
            return next;
        }
    }

    /// <summary>Forgets an artifact: removes it from the manifest first, then deletes its files or folders.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <param name="artifact">The artifact name.</param>
    /// <param name="relativePaths">The files or folders to delete.</param>
    public void RemoveArtifact(string fingerprint, string artifact, params string[] relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        if (Directory.Exists(DirectoryFor(fingerprint)))
        {
            UpdateManifest(fingerprint, m => m.WithoutArtifact(artifact));
        }

        foreach (var relative in relativePaths)
        {
            TryDeletePath(PathOf(fingerprint, relative));
        }

        MeasureEntry(KeyOf(fingerprint));
    }

    /// <summary>Records an access for the LRU, rewriting the manifest at most once per <see cref="TouchInterval"/>.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    public void Touch(string fingerprint)
    {
        var key = KeyOf(fingerprint);
        var path = PathOf(fingerprint, ManifestFile);
        lock (_gate)
        {
            var now = _utcNow();
            if (_touched.TryGetValue(key, out var last) && now - last < TouchInterval)
            {
                return;
            }

            _touched[key] = now;
            if (ReadManifestFile(path) is not { } manifest || now - manifest.LastAccessUtc < TouchInterval)
            {
                return;
            }

            try
            {
                WriteManifestFile(path, manifest with { LastAccessUtc = now });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The access time only steers the LRU; another instance may hold the file for a moment.
            }
        }
    }

    /// <summary>Scans every media folder, refreshes <see cref="UsedBytes"/> and returns the entries.</summary>
    /// <returns>Every entry with its size and last access.</returns>
    public IReadOnlyList<CacheEntryUsage> Measure()
    {
        var entries = new List<CacheEntryUsage>();
        if (Directory.Exists(Root))
        {
            foreach (var shard in SafeEnumerateDirectories(Root))
            {
                foreach (var folder in SafeEnumerateDirectories(shard))
                {
                    entries.Add(Usage(folder));
                }
            }
        }

        lock (_gate)
        {
            _sizes.Clear();
            foreach (var entry in entries)
            {
                _sizes[entry.Key] = entry.Bytes;
            }
        }

        RaiseUsageChanged();
        return entries;
    }

    /// <summary>
    /// Deletes abandoned <c>*.tmp-*</c> files and folders in every media folder (the startup cleanup, TECH_BRIEF
    /// 5.4). Only temps older than <paramref name="minAge"/> go, so another instance's running job keeps its temp.
    /// </summary>
    /// <param name="minAge">The minimum age, or null for <see cref="WorkDirectory.StaleTempAge"/>.</param>
    /// <returns>The number of temps deleted.</returns>
    public int DeleteStaleTemps(TimeSpan? minAge = null)
    {
        if (!Directory.Exists(Root))
        {
            return 0;
        }

        var age = minAge ?? WorkDirectory.StaleTempAge;
        var now = _utcNow();
        var deleted = 0;
        foreach (var shard in SafeEnumerateDirectories(Root))
        {
            foreach (var folder in SafeEnumerateDirectories(shard))
            {
                IEnumerable<string> children;
                try
                {
                    children = [.. Directory.EnumerateFileSystemEntries(folder)];
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (var child in children)
                {
                    if (!Path.GetFileName(child).Contains(TempMarker, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    try
                    {
                        var written = Directory.Exists(child) ? Directory.GetLastWriteTimeUtc(child) : File.GetLastWriteTimeUtc(child);
                        if (now - written >= age && TryDeletePath(child))
                        {
                            deleted++;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // In use; the next start tries again.
                    }
                }
            }
        }

        return deleted;
    }

    /// <summary>
    /// The LRU (TECH_BRIEF 6.1): measures every entry and deletes the least recently used unprotected ones until the
    /// total is at or below <see cref="LimitBytes"/>. Runs at startup and after each job.
    /// </summary>
    /// <param name="protectedFingerprints">Fingerprints of the media the open project uses.</param>
    /// <returns>The keys of the deleted entries.</returns>
    public IReadOnlyList<string> Evict(IEnumerable<string> protectedFingerprints)
    {
        ArgumentNullException.ThrowIfNull(protectedFingerprints);
        var keep = protectedFingerprints.Where(f => !string.IsNullOrWhiteSpace(f)).Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        var entries = Measure();
        var removed = new List<string>();
        foreach (var entry in SelectEvictions(entries, LimitBytes, keep))
        {
            if (TryDeletePath(Path.Combine(Root, entry.Key[..2], entry.Key)))
            {
                removed.Add(entry.Key);
            }
        }

        if (removed.Count > 0)
        {
            lock (_gate)
            {
                foreach (var key in removed)
                {
                    _sizes.Remove(key);
                    _touched.Remove(key);
                }
            }

            RaiseUsageChanged();
        }

        return removed;
    }

    /// <summary>Deletes every unprotected entry ("Clear cache").</summary>
    /// <param name="protectedFingerprints">Fingerprints of the media the open project uses.</param>
    /// <returns>The keys of the deleted entries.</returns>
    public IReadOnlyList<string> Clear(IEnumerable<string> protectedFingerprints)
    {
        var limit = LimitBytes;
        try
        {
            LimitBytes = 0;
            return Evict(protectedFingerprints);
        }
        finally
        {
            LimitBytes = limit;
        }
    }

    /// <summary>
    /// "Clear cache" (DESIGN 10), off the calling thread: deletes every media folder except those of
    /// <paramref name="busyFingerprints"/> (media a running job reads or writes) and those holding a temp younger than
    /// <see cref="WorkDirectory.StaleTempAge"/> (a job of another instance writing into it). Each folder loses its
    /// manifest first, so one that is only partly deleted (a file held open by a player) never claims an artifact it
    /// lost. The pre-M5 poster folder <c>&lt;cache&gt;/posters</c> goes too; the fingerprint memo and the capability
    /// cache stay.
    /// </summary>
    /// <param name="busyFingerprints">Fingerprints of the media in use by running jobs.</param>
    /// <param name="cancellationToken">Cancels before the next folder.</param>
    /// <returns>The keys of the deleted entries.</returns>
    public Task<IReadOnlyList<string>> ClearAsync(IEnumerable<string> busyFingerprints, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(busyFingerprints);
        var keep = busyFingerprints.Where(f => !string.IsNullOrWhiteSpace(f)).Select(KeyOf).ToHashSet(StringComparer.Ordinal);
        return Task.Run(() => ClearCore(keep, cancellationToken), cancellationToken);
    }

    private IReadOnlyList<string> ClearCore(HashSet<string> keep, CancellationToken cancellationToken)
    {
        var removed = new List<string>();
        if (Directory.Exists(Root))
        {
            var now = _utcNow();
            foreach (var shard in SafeEnumerateDirectories(Root))
            {
                foreach (var folder in SafeEnumerateDirectories(shard))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = Path.GetFileName(folder);
                    if (keep.Contains(key) || HasFreshTemp(folder, now))
                    {
                        continue;
                    }

                    lock (_gate)
                    {
                        TryDeletePath(Path.Combine(folder, ManifestFile));
                    }

                    if (TryDeletePath(folder))
                    {
                        removed.Add(key);
                    }
                }

                if (!SafeEnumerateDirectories(shard).Any())
                {
                    try
                    {
                        Directory.Delete(shard);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // A file appeared meanwhile; the folder stays.
                    }
                }
            }
        }

        TryDeletePath(Path.Combine(CacheRoot, "posters"));
        lock (_gate)
        {
            foreach (var key in removed)
            {
                _touched.Remove(key);
            }
        }

        Measure();
        return removed;
    }

    private static bool HasFreshTemp(string folder, DateTime now)
    {
        try
        {
            foreach (var child in Directory.EnumerateFileSystemEntries(folder))
            {
                if (!Path.GetFileName(child).Contains(TempMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                var written = Directory.Exists(child) ? Directory.GetLastWriteTimeUtc(child) : File.GetLastWriteTimeUtc(child);
                if (now - written < WorkDirectory.StaleTempAge)
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable: treat as busy rather than delete under someone's feet.
            return true;
        }

        return false;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string path)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(path)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static long FolderBytes(string folder)
    {
        long total = 0;
        try
        {
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try
                {
                    total += file.Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Deleted while enumerating.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A partial total is fine for the LRU.
        }

        return total;
    }

    private static CacheManifest? ReadManifestFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize(stream, CacheJsonContext.Default.CacheManifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A missing or corrupt manifest means no artifact counts as present; the next commit rewrites it.
            return null;
        }
    }

    private static void WriteManifestFile(string path, CacheManifest manifest)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, CacheJsonContext.Default.CacheManifest);
        var temp = Path.Combine(Path.GetDirectoryName(path)!, "manifest" + TempMarker + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            MoveFileWithRetry(temp, path);
        }
        finally
        {
            TryDeletePath(temp);
        }
    }

    private static void MoveFileWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < MoveAttempts)
            {
                // A reader in another process may hold the target without delete sharing for a moment.
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void MoveIntoPlace(string temp, string final)
    {
        if (Directory.Exists(temp))
        {
            if (Directory.Exists(final) && !TryDeletePath(final))
            {
                // Held open by a reader: keep the existing folder (same artifact version).
                TryDeletePath(temp);
                return;
            }

            try
            {
                Directory.Move(temp, final);
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && Directory.Exists(final))
            {
                TryDeletePath(temp);
            }

            return;
        }

        if (!File.Exists(temp))
        {
            throw new FileNotFoundException("The temp artifact does not exist.", temp);
        }

        try
        {
            MoveFileWithRetry(temp, final);
        }
        catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && File.Exists(final))
        {
            TryDeletePath(temp);
        }
    }

    private static bool TryDeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use; the startup cleanup or the next eviction removes it.
            return false;
        }
    }

    private CacheEntryUsage Usage(string folder)
    {
        var key = Path.GetFileName(folder);
        var manifest = ReadManifestFile(Path.Combine(folder, ManifestFile));
        DateTime last;
        if (manifest is not null && manifest.LastAccessUtc != default)
        {
            last = manifest.LastAccessUtc;
        }
        else
        {
            try
            {
                last = Directory.GetLastWriteTimeUtc(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = DateTime.MinValue;
            }
        }

        return new CacheEntryUsage(key, FolderBytes(folder), last);
    }

    private void MeasureEntry(string key)
    {
        var folder = Path.Combine(Root, key[..2], key);
        var bytes = Directory.Exists(folder) ? FolderBytes(folder) : 0;
        lock (_gate)
        {
            if (bytes == 0 && !Directory.Exists(folder))
            {
                _sizes.Remove(key);
            }
            else
            {
                _sizes[key] = bytes;
            }
        }

        RaiseUsageChanged();
    }

    private void RaiseUsageChanged()
    {
        try
        {
            UsageChanged?.Invoke();
        }
        catch (Exception)
        {
            // A failing handler must not break the cache.
        }
    }
}
