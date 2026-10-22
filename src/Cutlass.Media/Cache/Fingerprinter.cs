using System.Globalization;
using System.Text.Json;
using Cutlass.Media.Services;

namespace Cutlass.Media.Cache;

/// <summary>
/// Content fingerprints with a memo (TECH_BRIEF 6.2). The fingerprint is <c>sha256:</c> plus the hex SHA-256 over the
/// file size (8 bytes little-endian) and the first, middle and last 1 MiB (the whole file under 3 MiB); path and mtime
/// are excluded, so caches survive renames and relink can match by content. The memo <c>fingerprints.json</c>, keyed
/// by <c>path|size|mtimeTicks</c>, avoids rehashing an unchanged file. Thread-safe; call it off the UI thread.
/// </summary>
public sealed class Fingerprinter
{
    /// <summary>The memo file name in the cache root.</summary>
    public const string MemoFileName = "fingerprints.json";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, string>? _memo;

    /// <summary>Creates a fingerprinter.</summary>
    /// <param name="memoFile">The memo file (normally <c>&lt;cache&gt;/fingerprints.json</c>), or null to memoise in memory only.</param>
    public Fingerprinter(string? memoFile)
    {
        MemoFile = memoFile is null ? null : Path.GetFullPath(memoFile);
    }

    /// <summary>The memo file, or null.</summary>
    public string? MemoFile { get; }

    /// <summary>How many files were hashed (memo misses) by this instance.</summary>
    public int HashCount { get; private set; }

    /// <summary>The memo key of a file state: <c>fullPath|size|mtimeTicks</c>.</summary>
    /// <param name="fullPath">The normalised full path.</param>
    /// <param name="size">The size in bytes.</param>
    /// <param name="mtimeTicks">The last write time (UTC ticks).</param>
    /// <returns>The key.</returns>
    public static string MemoKey(string fullPath, long size, long mtimeTicks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var path = OperatingSystem.IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
        return string.Create(CultureInfo.InvariantCulture, $"{path}|{size}|{mtimeTicks}");
    }

    /// <summary>Computes the fingerprint without the memo (the same algorithm the importer uses).</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The fingerprint.</returns>
    public static Task<string> ComputeAsync(string path, CancellationToken cancellationToken) =>
        MediaImporter.ComputeFingerprintAsync(path, cancellationToken);

    /// <summary>Returns a file's fingerprint from the memo, or hashes it and remembers the result.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="FileNotFoundException">When the file does not exist.</exception>
    public async Task<string> GetAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The media file does not exist.", full);
        }

        var key = MemoKey(full, info.Length, info.LastWriteTimeUtc.Ticks);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var memo = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (memo.TryGetValue(key, out var known))
            {
                return known;
            }
        }
        finally
        {
            _gate.Release();
        }

        var fingerprint = await Task.Run(() => ComputeAsync(full, cancellationToken), cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            HashCount++;
            var memo = await LoadAsync(cancellationToken).ConfigureAwait(false);
            memo[key] = fingerprint;
            await SaveAsync(memo, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return fingerprint;
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        if (_memo is not null)
        {
            return _memo;
        }

        _memo = new Dictionary<string, string>(StringComparer.Ordinal);
        if (MemoFile is null || !File.Exists(MemoFile))
        {
            return _memo;
        }

        try
        {
            await using var stream = new FileStream(MemoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            var loaded = await JsonSerializer.DeserializeAsync(stream, CacheJsonContext.Default.DictionaryStringString, cancellationToken).ConfigureAwait(false);
            if (loaded is not null)
            {
                foreach (var (k, v) in loaded)
                {
                    _memo[k] = v;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A broken memo only costs rehashing.
        }

        return _memo;
    }

    private async Task SaveAsync(Dictionary<string, string> memo, CancellationToken cancellationToken)
    {
        if (MemoFile is null)
        {
            return;
        }

        var temp = MemoFile + MediaCache.TempMarker + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MemoFile)!);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, memo, CacheJsonContext.Default.DictionaryStringString, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, MemoFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another instance may be writing the memo; ours is kept in memory and saved with the next miss.
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Removed by the startup cleanup later.
            }
        }
    }
}
