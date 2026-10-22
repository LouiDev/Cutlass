using System.Text.Json;
using Cutlass.Media.Cache;

namespace Cutlass.Tests.Media.Cache;

/// <summary>The media cache layout, manifest, atomic commits, size accounting and LRU (TECH_BRIEF 6.1, 11.1).</summary>
public sealed class MediaCacheTests : IDisposable
{
    private const string FpA = "sha256:aa11000000000000000000000000000000000000000000000000000000000000";
    private const string FpB = "sha256:bb22000000000000000000000000000000000000000000000000000000000000";
    private const string FpC = "sha256:cc33000000000000000000000000000000000000000000000000000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cutlass-cache-" + Guid.NewGuid().ToString("N")[..12]);
    private DateTime _now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing written.
        }
    }

    [Fact]
    public void FoldersFollowTheShardedLayout()
    {
        var cache = NewCache();

        Assert.Equal(Path.Combine(_root, "media", "aa", "aa11" + new string('0', 60)), cache.DirectoryFor(FpA));
        Assert.Equal("aa11" + new string('0', 60), MediaCache.KeyOf(FpA));

        // Anything that is not lowercase hex is hashed into a safe key.
        var odd = MediaCache.KeyOf("sha256:NOT HEX/..");
        Assert.Equal(64, odd.Length);
        Assert.True(odd.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'));
    }

    [Fact]
    public void ManifestRoundTripsWithTheDocumentedFieldNames()
    {
        var cache = NewCache();
        var temp = cache.NewTempFile(FpA, "proxy540", ".mov");
        File.WriteAllText(temp, "movie");
        cache.Commit(FpA, CacheArtifacts.Proxy, [(temp, CacheArtifacts.ProxyFile)], m => m with { ProxyMapping = ProxyMapping.Exact });
        CommitFile(cache, FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile, "idx");

        var json = File.ReadAllText(Path.Combine(cache.DirectoryFor(FpA), "manifest.json"));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(FpA, root.GetProperty("fingerprint").GetString());
        Assert.Equal("N-126308", root.GetProperty("ffmpegBuild").GetString());
        Assert.Equal(1, root.GetProperty("artifacts").GetProperty("proxy540").GetInt32());
        Assert.Equal(CacheArtifacts.VersionOf(CacheArtifacts.Index), root.GetProperty("artifacts").GetProperty("index").GetInt32());
        Assert.Equal("exact", root.GetProperty("proxyMapping").GetString());
        Assert.Equal(_now, root.GetProperty("lastAccessUtc").GetDateTime().ToUniversalTime());

        var manifest = NewCache().ReadManifest(FpA);
        Assert.NotNull(manifest);
        Assert.Equal(FpA, manifest.Fingerprint);
        Assert.Equal(ProxyMapping.Exact, manifest.ProxyMapping);
        Assert.True(manifest.HasCurrent(CacheArtifacts.Proxy));
        Assert.True(manifest.HasCurrent(CacheArtifacts.Index));
        Assert.False(manifest.HasCurrent(CacheArtifacts.Peaks));
        Assert.Equal(_now, manifest.LastAccessUtc);
    }

    [Fact]
    public void AManifestWithUnknownFieldsAndOldVersionsLoadsAndOnlyTheBumpedArtifactCountsAsMissing()
    {
        var cache = NewCache();
        Directory.CreateDirectory(cache.DirectoryFor(FpA));
        File.WriteAllText(cache.PathOf(FpA, CacheArtifacts.IndexFile), "x");
        File.WriteAllText(cache.PathOf(FpA, CacheArtifacts.PeaksFile), "x");
        File.WriteAllText(
            cache.PathOf(FpA, "manifest.json"),
            "{\"fingerprint\":\"" + FpA + "\",\"future\":42,\"artifacts\":{\"index\":" + CacheArtifacts.VersionOf(CacheArtifacts.Index).ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"peaks\":0},\"proxyMapping\":\"rejected\"}");

        var manifest = cache.ReadManifest(FpA);

        Assert.NotNull(manifest);
        Assert.Equal(ProxyMapping.Rejected, manifest.ProxyMapping);
        Assert.True(cache.HasArtifact(FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile));
        Assert.False(cache.HasArtifact(FpA, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile));
        Assert.Null(cache.TryGetArtifact(FpA, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile));
    }

    [Fact]
    public void ACorruptManifestMeansNothingIsPresentAndTheNextCommitRepairsIt()
    {
        var cache = NewCache();
        Directory.CreateDirectory(cache.DirectoryFor(FpA));
        File.WriteAllText(cache.PathOf(FpA, CacheArtifacts.IndexFile), "x");
        File.WriteAllText(cache.PathOf(FpA, "manifest.json"), "{ not json");

        Assert.Null(cache.ReadManifest(FpA));
        Assert.False(cache.HasArtifact(FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile));

        CommitFile(cache, FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile, "idx");
        Assert.True(cache.HasArtifact(FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile));
    }

    [Fact]
    public void AnArtifactCountsOnlyOnceRenamedAndRecorded()
    {
        var cache = NewCache();
        var temp = cache.NewTempFile(FpA, "frames", ".idx");
        Assert.Contains(".tmp-", Path.GetFileName(temp), StringComparison.Ordinal);
        Assert.EndsWith(".idx", temp, StringComparison.Ordinal);
        File.WriteAllText(temp, "data");

        Assert.False(cache.HasArtifact(FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile));
        cache.Commit(FpA, CacheArtifacts.Index, temp, CacheArtifacts.IndexFile);

        Assert.False(File.Exists(temp));
        Assert.Equal("data", File.ReadAllText(cache.TryGetArtifact(FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile)!));
    }

    [Fact]
    public void FolderArtifactsReplaceAnOlderFolder()
    {
        var cache = NewCache();
        for (var round = 1; round <= 2; round++)
        {
            var temp = cache.NewTempDirectory(FpA, "thumbs");
            File.WriteAllText(Path.Combine(temp, "sheet_001.jpg"), "round" + round);
            cache.Commit(FpA, CacheArtifacts.Thumbnails, temp, CacheArtifacts.ThumbnailsFolder);
            Assert.False(Directory.Exists(temp));
        }

        var folder = cache.TryGetArtifact(FpA, CacheArtifacts.Thumbnails, CacheArtifacts.ThumbnailsFolder);
        Assert.NotNull(folder);
        Assert.Equal("round2", File.ReadAllText(Path.Combine(folder, "sheet_001.jpg")));
        Assert.Single(Directory.GetFileSystemEntries(cache.DirectoryFor(FpA)), e => Path.GetFileName(e).StartsWith("thumbs", StringComparison.Ordinal));
    }

    [Fact]
    public void RemovingAnArtifactForgetsItAndDeletesItsFiles()
    {
        var cache = NewCache();
        CommitFile(cache, FpA, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile, "peaks");

        cache.RemoveArtifact(FpA, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile);

        Assert.False(File.Exists(cache.PathOf(FpA, CacheArtifacts.PeaksFile)));
        Assert.False(cache.ReadManifest(FpA)!.HasCurrent(CacheArtifacts.Peaks));
    }

    [Fact]
    public void UsageCountsEveryFileAndFollowsCommitsAndEvictions()
    {
        var cache = NewCache();
        var changes = 0;
        cache.UsageChanged += () => Interlocked.Increment(ref changes);

        CommitFile(cache, FpA, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile, new string('a', 1000));
        var manifestA = new FileInfo(cache.PathOf(FpA, "manifest.json")).Length;
        Assert.Equal(1000 + manifestA, cache.UsedBytes);
        Assert.True(changes > 0);

        var fresh = NewCache();
        Assert.Equal(0, fresh.UsedBytes);
        var entries = fresh.Measure();
        Assert.Single(entries);
        Assert.Equal(1000 + manifestA, fresh.UsedBytes);
        Assert.Equal(_now, entries[0].LastAccessUtc);
    }

    [Fact]
    public void SelectEvictionsRemovesTheLeastRecentlyUsedUntilUnderTheLimit()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CacheEntryUsage[] entries =
        [
            new("new", 400, t0.AddDays(3)),
            new("old", 300, t0),
            new("mid", 200, t0.AddDays(1)),
            new("older-protected", 500, t0.AddDays(-1)),
        ];

        // Total 1400. Limit 800: skip the protected oldest, drop "old" (1100), then "mid" (900), then "new" (500).
        var evicted = MediaCache.SelectEvictions(entries, 800, new HashSet<string> { "older-protected" });
        Assert.Equal(new[] { "old", "mid", "new" }, evicted.Select(e => e.Key));

        Assert.Equal(new[] { "older-protected" }, MediaCache.SelectEvictions(entries, 1000, new HashSet<string>()).Select(e => e.Key));
        Assert.Empty(MediaCache.SelectEvictions(entries, 1400, new HashSet<string>()));
        Assert.Empty(MediaCache.SelectEvictions([], 0, new HashSet<string>()));
    }

    [Fact]
    public void EvictTrimsRealFoldersOldestFirstAndNeverTouchesProtectedMedia()
    {
        var cache = NewCache(limitBytes: long.MaxValue);
        CommitFile(cache, FpA, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile, new string('a', 5000));
        _now = _now.AddHours(1);
        CommitFile(cache, FpB, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile, new string('b', 5000));
        _now = _now.AddHours(1);
        CommitFile(cache, FpC, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile, new string('c', 5000));

        // A is the oldest but protected; touching B later than C makes C the next candidate.
        _now = _now.AddHours(1);
        Assert.NotNull(cache.TryGetArtifact(FpB, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile));
        cache.LimitBytes = 12_000;

        var removed = cache.Evict([FpA]);

        Assert.Equal(new[] { MediaCache.KeyOf(FpC) }, removed);
        Assert.True(Directory.Exists(cache.DirectoryFor(FpA)));
        Assert.True(Directory.Exists(cache.DirectoryFor(FpB)));
        Assert.False(Directory.Exists(cache.DirectoryFor(FpC)));
        Assert.True(cache.UsedBytes <= 12_000);

        // Clear keeps only the protected media.
        cache.Clear([FpA]);
        Assert.True(Directory.Exists(cache.DirectoryFor(FpA)));
        Assert.False(Directory.Exists(cache.DirectoryFor(FpB)));
    }

    [Fact]
    public void TouchRewritesTheAccessTimeAtMostOncePerInterval()
    {
        var cache = NewCache();
        CommitFile(cache, FpA, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile, "p");
        var written = _now;

        _now = written.AddSeconds(30);
        cache.Touch(FpA);
        Assert.Equal(written, cache.ReadManifest(FpA)!.LastAccessUtc);

        _now = written.AddMinutes(5);
        cache.Touch(FpA);
        Assert.Equal(_now, cache.ReadManifest(FpA)!.LastAccessUtc);
    }

    [Fact]
    public void StaleTempsAreDeletedAndArtifactsSurvive()
    {
        var cache = NewCache();
        CommitFile(cache, FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile, "idx");
        var oldFile = cache.NewTempFile(FpA, "proxy540", ".mov");
        File.WriteAllText(oldFile, "partial");
        var oldFolder = cache.NewTempDirectory(FpA, "thumbs");
        File.WriteAllText(Path.Combine(oldFolder, "sheet_001.jpg"), "partial");
        var fresh = cache.NewTempFile(FpA, "peaks", ".bin");
        File.WriteAllText(fresh, "running");

        // The clock says two hours later; the fresh temp gets a current write time so it counts as in use.
        _now = DateTime.UtcNow.AddHours(2);
        File.SetLastWriteTimeUtc(oldFile, _now.AddHours(-3));
        Directory.SetLastWriteTimeUtc(oldFolder, _now.AddHours(-3));
        File.SetLastWriteTimeUtc(fresh, _now.AddMinutes(-5));

        Assert.Equal(2, cache.DeleteStaleTemps());

        Assert.False(File.Exists(oldFile));
        Assert.False(Directory.Exists(oldFolder));
        Assert.True(File.Exists(fresh));
        Assert.True(cache.HasArtifact(FpA, CacheArtifacts.Index, CacheArtifacts.IndexFile));
    }

    private MediaCache NewCache(long limitBytes = MediaCache.DefaultLimitBytes) =>
        new(_root, "N-126308", limitBytes, () => _now);

    private static void CommitFile(MediaCache cache, string fingerprint, string artifact, string name, string content)
    {
        var temp = cache.NewTempFile(fingerprint, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name));
        File.WriteAllText(temp, content);
        cache.Commit(fingerprint, artifact, temp, name);
    }
}
