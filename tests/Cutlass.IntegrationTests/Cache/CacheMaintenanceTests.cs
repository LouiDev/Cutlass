using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Cache;
using Cutlass.Media.Services;

namespace Cutlass.IntegrationTests.Cache;

/// <summary>
/// Cache maintenance on disk (TECH_BRIEF 6.1, 6.5): LRU eviction over a small limit keeps the open project's media and
/// consistent manifests, "Clear cache" spares busy media, the poster lives in the media cache (with lazy migration
/// from the M4 folder), and <c>probe.json</c> spares ffprobe on a re-import.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class CacheMaintenanceTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public CacheMaintenanceTests(TestMediaFixture media)
    {
        _media = media;
    }

    [Fact]
    public void EvictionOverASmallLimitDropsTheLeastRecentlyUsedAndNeverTheOpenProject()
    {
        using var temp = new TempDirectory("cutlass-evict");
        var now = new DateTime(2026, 10, 22, 8, 0, 0, DateTimeKind.Utc);
        var cache = new MediaCache(temp.Path, "N-1", limitBytes: 0, () => now);
        var fingerprints = Enumerable.Range(0, 6).Select(i => "sha256:" + i.ToString("x2", System.Globalization.CultureInfo.InvariantCulture) + new string('0', 62)).ToList();
        foreach (var fingerprint in fingerprints)
        {
            // Each entry: two artifacts of 2000 bytes plus the manifest, one minute apart in access time.
            Commit(cache, fingerprint, CacheArtifacts.Index, CacheArtifacts.IndexFile, 2000);
            Commit(cache, fingerprint, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile, 2000);
            now = now.AddMinutes(1);
        }

        var entry = cache.Measure()[0].Bytes;
        cache.LimitBytes = (entry * 3) + 10;

        // The oldest entry belongs to the open project.
        var removed = cache.Evict([fingerprints[0]]);

        Assert.Equal([MediaCache.KeyOf(fingerprints[1]), MediaCache.KeyOf(fingerprints[2]), MediaCache.KeyOf(fingerprints[3])], removed.ToArray());
        Assert.True(cache.UsedBytes <= cache.LimitBytes);
        foreach (var kept in new[] { fingerprints[0], fingerprints[4], fingerprints[5] })
        {
            var manifest = cache.ReadManifest(kept);
            Assert.NotNull(manifest);
            Assert.Equal(kept, manifest.Fingerprint);
            Assert.True(cache.HasArtifact(kept, CacheArtifacts.Index, CacheArtifacts.IndexFile));
            Assert.True(cache.HasArtifact(kept, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile));
        }

        foreach (var gone in fingerprints.Skip(1).Take(3))
        {
            Assert.False(Directory.Exists(cache.DirectoryFor(gone)));
            Assert.Null(cache.ReadManifest(gone));
        }

        // Touching an entry moves it to the back of the line.
        now = now.AddHours(1);
        cache.Touch(fingerprints[4]);
        cache.LimitBytes = (entry * 2) + 10;
        Assert.Equal([MediaCache.KeyOf(fingerprints[5])], cache.Evict([fingerprints[0]]).ToArray());
    }

    [Fact]
    public async Task ClearCacheSparesBusyMediaAndRunningWrites()
    {
        using var temp = new TempDirectory("cutlass-clear");
        var cache = new MediaCache(temp.Path, "N-1");
        const string idle = "sha256:aa00000000000000000000000000000000000000000000000000000000000000";
        const string busy = "sha256:bb00000000000000000000000000000000000000000000000000000000000000";
        const string writing = "sha256:cc00000000000000000000000000000000000000000000000000000000000000";
        foreach (var fingerprint in new[] { idle, busy, writing })
        {
            Commit(cache, fingerprint, CacheArtifacts.Index, CacheArtifacts.IndexFile, 100);
        }

        // A job (perhaps of another instance) is writing a temp into this folder right now.
        await File.WriteAllTextAsync(cache.NewTempFile(writing, "proxy540", ".mov"), "in progress");
        var legacyPosters = Path.Combine(cache.CacheRoot, "posters");
        Directory.CreateDirectory(legacyPosters);
        await File.WriteAllTextAsync(Path.Combine(legacyPosters, "old.jpg"), "jpeg");

        var removed = await cache.ClearAsync([busy]);

        Assert.Equal([MediaCache.KeyOf(idle)], removed.ToArray());
        Assert.False(Directory.Exists(cache.DirectoryFor(idle)));
        Assert.True(cache.HasArtifact(busy, CacheArtifacts.Index, CacheArtifacts.IndexFile));
        Assert.True(cache.HasArtifact(writing, CacheArtifacts.Index, CacheArtifacts.IndexFile));
        Assert.False(Directory.Exists(legacyPosters));
        Assert.Equal(cache.Measure().Sum(e => e.Bytes), cache.UsedBytes);
        Assert.Equal(2, cache.Measure().Count);
    }

    [FfmpegFact]
    public async Task PostersLiveInTheMediaCacheAndOldPostersMoveThereLazily()
    {
        await using var kit = await CacheKit.CreateAsync();
        var h = kit.Preview.Harness;

        // The kit's own importer still uses the M4 folder, which leaves a poster to migrate.
        var asset = await kit.ImportAsync(_media.Cfr25Av);
        var legacy = kit.Preview.Posters.PathFor(asset);
        Assert.True(File.Exists(legacy));

        var posters = new PosterService(h.Runner, kit.BrokenToolset, kit.Preview.Paths, kit.Cache);
        var migrated = await posters.GetOrCreateAsync(asset, CancellationToken.None);

        Assert.Equal(kit.Cache.PathOf(asset.Fingerprint, CacheArtifacts.PosterFile), migrated);
        Assert.False(File.Exists(legacy));
        Assert.True(kit.Cache.HasArtifact(asset.Fingerprint, CacheArtifacts.Poster, CacheArtifacts.PosterFile));
        Assert.Equal(migrated, await posters.GetOrCreateAsync(asset, CancellationToken.None));

        // A new poster is rendered straight into the media cache.
        var other = await kit.ImportAsync(_media.Cfr2997Av);
        File.Delete(kit.Preview.Posters.PathFor(other));
        var rendered = await new PosterService(h.Runner, h.Toolset, kit.Preview.Paths, kit.Cache).GetOrCreateAsync(other, CancellationToken.None);
        Assert.Equal(kit.Cache.PathOf(other.Fingerprint, CacheArtifacts.PosterFile), rendered);
        Assert.True(new FileInfo(rendered!).Length > 0);
        Assert.Empty(Directory.GetFiles(kit.Cache.DirectoryFor(other.Fingerprint), "*" + MediaCache.TempMarker + "*"));
    }

    [FfmpegFact]
    public async Task AReimportReadsProbeJsonInsteadOfRunningFfprobe()
    {
        await using var kit = await CacheKit.CreateAsync();
        var h = kit.Preview.Harness;
        var fingerprints = new Fingerprinter(Path.Combine(kit.Cache.CacheRoot, Fingerprinter.MemoFileName));

        var first = new ProbeService(h.Runner, h.Toolset, h.Work, kit.Cache, fingerprints);
        var probed = await first.ProbeAsync(_media.Cfr25Av, CancellationToken.None);
        Assert.Equal(1, first.FfprobeRuns);
        var fingerprint = await fingerprints.GetAsync(_media.Cfr25Av, CancellationToken.None);
        Assert.True(kit.Cache.HasArtifact(fingerprint, CacheArtifacts.Probe, CacheArtifacts.ProbeFile));

        // A new session (fresh in-memory memo) with an ffprobe that cannot start still gets the result.
        var second = new ProbeService(h.Runner, kit.BrokenToolset, h.Work, kit.Cache, new Fingerprinter(fingerprints.MemoFile));
        var cached = await second.ProbeAsync(_media.Cfr25Av, CancellationToken.None);

        Assert.Equal(0, second.FfprobeRuns);
        Assert.Equal(probed.Info, cached.Info);
        Assert.Equal(probed.Json, cached.Json);
    }

    private static void Commit(MediaCache cache, string fingerprint, string artifact, string name, int bytes)
    {
        var temp = cache.NewTempFile(fingerprint, Path.GetFileNameWithoutExtension(name), Path.GetExtension(name));
        File.WriteAllBytes(temp, new byte[bytes]);
        cache.Commit(fingerprint, artifact, temp, name);
    }
}
