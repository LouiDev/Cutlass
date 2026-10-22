using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Jobs;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;

namespace Cutlass.IntegrationTests.Cache;

/// <summary>
/// Thumbnails, waveforms and the <c>frames.idx</c> disk cache against real ffmpeg (TECH_BRIEF 6.1, 6.4, 6.5, 11.2),
/// plus cache survival across service instances and planted temp files.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class ArtifactTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ArtifactTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task ThumbnailSheetsHaveTheDocumentedGeometryAndShowTheSourceTimeOfEachTile()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Oracle25Intra);

        var sheet = await kit.Thumbnails.GetSheetAsync(asset, null, CancellationToken.None);

        // 20 s: interval 1 s, 20 thumbnails on one 10x10 sheet of 1600x900.
        Assert.NotNull(sheet);
        Assert.Equal(1_000_000, sheet.IntervalMicros);
        Assert.Equal(20, sheet.Count);
        Assert.Equal(1, sheet.SheetCount);
        Assert.Equal(kit.Cache.PathOf(asset.Fingerprint, "thumbs"), sheet.Directory);
        Assert.True(File.Exists(Path.Combine(sheet.Directory, "thumbs.json")));
        Assert.False(File.Exists(sheet.SheetPath(1)));

        var probe = await kit.Preview.Probe.ProbeAsync(sheet.SheetPath(0), CancellationToken.None);
        Assert.Equal(ThumbnailSheet.SheetWidth, probe.Info.Video!.Width);
        Assert.Equal(ThumbnailSheet.SheetHeight, probe.Info.Video.Height);

        // Each tile is a half-size oracle frame: tile i shows source time i s, which is oracle frame 25 i.
        var run = await kit.Preview.Harness.Runner.RunAsync(
            kit.Preview.Harness.Ffmpeg(["-hide_banner", "-nostdin", "-v", "error", "-i", sheet.SheetPath(0), "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1"]),
            TimeSpan.FromSeconds(30),
            CancellationToken.None);
        run.EnsureSuccess();
        Assert.Equal(ThumbnailSheet.SheetWidth * ThumbnailSheet.SheetHeight, run.Stdout.Length);
        for (var i = 0; i < sheet.Count; i++)
        {
            var (_, x, y, w, h) = sheet.Tile(i);
            var tile = run.Stdout.AsSpan((y * ThumbnailSheet.SheetWidth) + x);
            Assert.Equal(TestMediaFixture.ExpectedOracleValue(25L * i), OracleReader.DecodeGray(tile, w, h, ThumbnailSheet.SheetWidth));
        }

        // Unused tiles of the sheet stay black.
        Assert.True(run.Stdout[(5 * 90 * 1600) + (5 * 160) + 80] < 40);
    }

    [FfmpegFact]
    public async Task ThumbnailsLoadFromTheCacheInANewInstanceWithoutFfmpeg()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Cfr2997Av);
        var first = await kit.Thumbnails.GetSheetAsync(asset, null, CancellationToken.None);
        // 3.003 s: count = ceil(3.003 / 1) = 4 (TECH_BRIEF 6.5), on one sheet.
        Assert.NotNull(first);
        Assert.Equal(4, first.Count);

        var h = kit.Preview.Harness;
        var offline = new ThumbnailService(h.Runner, kit.BrokenToolset, h.Work, new MediaCache(kit.Cache.CacheRoot));
        Assert.Null(offline.TryGet(asset));
        var second = await offline.GetSheetAsync(asset, null, CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(first, offline.TryGet(asset));
    }

    [FfmpegFact]
    public async Task TheToneWaveformHasConstantPeaksOfTheSineAmplitude()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Tone);
        var fractions = new List<double>();

        var pyramid = await kit.Waveforms.GetAsync(asset, new SyncProgress(fractions.Add), CancellationToken.None);

        // sine=f=1000 has amplitude 1/8 (4096); 10 s at 100 peaks per second.
        Assert.NotNull(pyramid);
        Assert.Equal(1000, pyramid.Count(0));
        for (var i = 1; i < pyramid.Count(0) - 1; i++)
        {
            var (min, max) = pyramid.Peak(0, i);
            Assert.InRange(max, 3900, 4300);
            Assert.InRange(min, -4300, -3900);
        }

        Assert.Equal(11, pyramid.LevelCount);
        var whole = pyramid.PeaksFor(0, 48000L * 10, 50);
        Assert.All(whole, b => Assert.InRange(b.Max, 3900, 4300));
        Assert.NotEmpty(fractions);
        Assert.Equal(1.0, fractions[^1]);
        Assert.True(File.Exists(kit.Cache.PathOf(asset.Fingerprint, "peaks.bin")));
    }

    [FfmpegFact]
    public async Task LateAudioIsDrawnWhereItIsHeard()
    {
        // The audio of delayed_audio.mov starts 0.5 s after the format start, the origin of source time (TECH_BRIEF
        // 6.5): its first impulse plays at 0.5 s, so it must be peak 50, not peak 0.
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.DelayedAudio);

        var pyramid = await kit.Waveforms.GetAsync(asset, null, CancellationToken.None);

        Assert.NotNull(pyramid);
        var loud = Enumerable.Range(0, pyramid.Count(0)).Where(i => pyramid.Peak(0, i).Max > 5000).ToList();
        Assert.NotEmpty(loud);
        Assert.InRange(loud[0], 49, 50);
        Assert.All(Enumerable.Range(0, 48), i => Assert.True(pyramid.Peak(0, i).Max <= 50, $"peak {i} should be silent"));
    }

    [FfmpegFact]
    public async Task TheImpulseWaveformPeaksOncePerSecondAndIsSilentElsewhere()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Impulse);

        var pyramid = await kit.Waveforms.GetAsync(asset, null, CancellationToken.None);

        // An impulse at sample 0 of every second lands in level 0 peak 100 k; the resampler rings into the peak
        // before it, and every other peak is silent.
        Assert.NotNull(pyramid);
        Assert.Equal(2000, pyramid.Count(0));
        for (var i = 0; i < pyramid.Count(0); i++)
        {
            var (min, max) = pyramid.Peak(0, i);
            var nearest = (int)Math.Round(i / 100.0) * 100;
            if (i % 100 == 0)
            {
                Assert.True(max > 5000, $"peak {i}: {max}");
            }
            else if (Math.Abs(i - nearest) > 1)
            {
                Assert.True(Math.Max(Math.Abs((int)min), max) <= 50, $"peak {i}: {min}..{max}");
            }
        }

        // Drawn at one bin per second, every bin shows its impulse.
        var bins = pyramid.PeaksFor(0, 48000L * 20, 20);
        Assert.All(bins, b => Assert.True(b.Max > 5000));
    }

    [FfmpegFact]
    public async Task WaveformsLoadFromTheCacheInANewInstanceWithoutFfmpeg()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Tone);
        var first = await kit.Waveforms.GetAsync(asset, null, CancellationToken.None);

        var h = kit.Preview.Harness;
        var offline = new WaveformService(h.Runner, kit.BrokenToolset, h.Work, new MediaCache(kit.Cache.CacheRoot));
        var second = await offline.GetAsync(asset, null, CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.LevelCount, second.LevelCount);
        Assert.Equal(Enumerable.Range(0, first.Count(0)).Select(i => first.Peak(0, i)), Enumerable.Range(0, second.Count(0)).Select(i => second.Peak(0, i)));

        // Media without audio has no waveform, and media without video no thumbnails.
        var video = await kit.ImportAsync(_media.Oracle25Intra);
        Assert.Null(await kit.Waveforms.GetAsync(video, null, CancellationToken.None));
        Assert.Null(await kit.Thumbnails.GetSheetAsync(asset, null, CancellationToken.None));
    }

    [FfmpegFact]
    public async Task FramesIdxRoundTripEqualsTheInMemoryIndex()
    {
        await using var kit = await CacheKit.CreateAsync();
        foreach (var path in new[] { _media.Oracle25LongGop, _media.OracleVfr, _media.OffsetStart })
        {
            var asset = await kit.ImportAsync(path);
            var memory = await kit.Preview.FrameIndex.GetAsync(asset, CancellationToken.None);
            Assert.NotNull(memory);

            var cached = await kit.CachedIndex.GetAsync(asset, CancellationToken.None);
            var file = kit.Cache.PathOf(asset.Fingerprint, CacheArtifacts.IndexFile);
            Assert.True(FrameIndexService.TryReadIndexFile(file, out var timeBase, out var fromDisk));
            Assert.Equal(asset.Info.Video!.TimeBase, timeBase);
            Assert.Equal(memory.Pts.ToArray(), cached!.Pts.ToArray());
            Assert.Equal(memory.Pts.ToArray(), fromDisk!.Pts.ToArray());

            // A new service instance with a broken ffprobe answers from frames.idx without a packet pass.
            var h = kit.Preview.Harness;
            var reopened = new FrameIndexService(h.Runner, kit.BrokenToolset, h.Work, new MediaCache(kit.Cache.CacheRoot));
            var again = await reopened.GetAsync(asset, CancellationToken.None);
            Assert.Equal(0, reopened.PacketPassCount);
            Assert.Equal(memory.Pts.ToArray(), again!.Pts.ToArray());
            Assert.Equal(memory.TimeBase, again.TimeBase);
        }
    }

    [FfmpegFact]
    public async Task TheCacheSurvivesANewInstanceAndAPlantedTempFile()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Cfr25Av);
        var index = await kit.CachedIndex.GetAsync(asset, CancellationToken.None);
        Assert.NotNull(index);

        // A crashed writer left a half-written frames index and a thumbnail folder behind.
        var plantedFile = kit.Cache.NewTempFile(asset.Fingerprint, "frames", ".idx");
        await File.WriteAllBytesAsync(plantedFile, [1, 2, 3]);
        var plantedFolder = kit.Cache.NewTempDirectory(asset.Fingerprint, "thumbs");
        await File.WriteAllTextAsync(Path.Combine(plantedFolder, "sheet_001.jpg"), "partial");
        File.SetLastWriteTimeUtc(plantedFile, DateTime.UtcNow.AddHours(-2));
        Directory.SetLastWriteTimeUtc(plantedFolder, DateTime.UtcNow.AddHours(-2));

        var restarted = new MediaCache(kit.Cache.CacheRoot);
        Assert.Equal(2, restarted.DeleteStaleTemps());
        Assert.False(File.Exists(plantedFile));
        Assert.False(Directory.Exists(plantedFolder));

        var h = kit.Preview.Harness;
        var reopened = new FrameIndexService(h.Runner, kit.BrokenToolset, h.Work, restarted);
        var again = await reopened.GetAsync(asset, CancellationToken.None);
        Assert.Equal(index.Pts.ToArray(), again!.Pts.ToArray());
        Assert.True(restarted.Measure().Single().Bytes > 0);
    }

    [FfmpegFact]
    public async Task ThePipelineRunsEveryJobOfAMediaItemAndAttachesTheProxy()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Cfr25Av);
        var (scheduler, pipeline) = kit.Pipeline();
        await using (scheduler)
        {
            using (pipeline)
            {
                pipeline.ProxyPolicy = ProxyPolicy.Always;
                var ready = new List<JobKind>();
                scheduler.ArtifactReady += (id, kind) =>
                {
                    lock (ready)
                    {
                        Assert.Equal(asset.Id, id);
                        ready.Add(kind);
                    }
                };

                var jobs = pipeline.Enqueue(asset, JobPriority.Visible);
                Assert.Equal(new[] { JobKind.FrameIndex, JobKind.Thumbnails, JobKind.Waveform, JobKind.Proxy }, jobs.Select(j => j.Kind));
                await scheduler.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(2));

                Assert.All(scheduler.Jobs, j => Assert.Equal(JobState.Completed, j.State));
                Assert.Equal(new[] { JobKind.FrameIndex, JobKind.Thumbnails, JobKind.Waveform, JobKind.Proxy }, ready.Order());
                Assert.NotNull(kit.Thumbnails.TryGet(asset));
                Assert.NotNull(kit.Waveforms.TryGet(asset));
                var proxy = kit.Preview.Catalog.Get(asset.Id)!.Proxy;
                Assert.NotNull(proxy);
                Assert.Equal(kit.Cache.PathOf(asset.Fingerprint, CacheArtifacts.ProxyFile), proxy.Path);
                Assert.Equal(ProxyMapping.Exact, kit.Cache.ReadManifest(asset.Fingerprint)!.ProxyMapping);

                // Re-queueing finished media answers every job from the cache.
                pipeline.Enqueue(asset);
                await scheduler.WhenIdleAsync().WaitAsync(TimeSpan.FromMinutes(1));
                Assert.All(scheduler.Jobs, j => Assert.Equal(JobState.Completed, j.State));
            }
        }
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
