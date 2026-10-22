using System.Globalization;
using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.Export;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.IntegrationTests.PreviewTests;
using Cutlass.Media.Cache;
using Cutlass.Media.Jobs;
using Cutlass.Media.Preview;
using Cutlass.Media.Services;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Cache;

/// <summary>
/// Proxies (TECH_BRIEF 6.6, 11.2): CFR oracles map <c>exact</c>, the proxy is attached to the catalog, and scrubbing
/// through the proxy is frame exact at 50 random frames, like the original (<c>FrameServerAccuracyTests</c>).
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class ProxyTests
{
    private const long ClipStart = 5;
    private const long ClipDuration = 240;
    private const int Samples = 50;
    private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(60);

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public ProxyTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task CfrSourcesMapExactlyIncludingAShiftedStartAndTheProxyIsAttached()
    {
        await using var kit = await CacheKit.CreateAsync();
        foreach (var path in new[] { _media.Oracle25Intra, _media.Oracle25LongGop, _media.Oracle2997, _media.OffsetStart })
        {
            var asset = await kit.ImportAsync(path);
            var (mapping, proxy) = await kit.Proxies.EnsureAsync(asset, null, CancellationToken.None);

            Assert.Equal(ProxyMapping.Exact, mapping);
            Assert.NotNull(proxy);
            Assert.Equal(kit.Cache.PathOf(asset.Fingerprint, CacheArtifacts.ProxyFile), proxy.Path);
            Assert.Same(proxy, kit.Preview.Catalog.Get(asset.Id)!.Proxy);
            Assert.Equal(kit.Preview.Catalog.Get(asset.Id)!.Original.Index!.Count, proxy.Index!.Count);

            // 540 lines, MJPEG, video only.
            var probe = await kit.Preview.Probe.ProbeAsync(proxy.Path, CancellationToken.None);
            Assert.Equal(540, probe.Info.Video!.Height);
            Assert.Equal("mjpeg", probe.Info.Video.Codec);
            Assert.Null(probe.Info.Audio);
            Assert.True(File.Exists(kit.Cache.PathOf(asset.Fingerprint, CacheArtifacts.ProxyIndexFile)));
        }
    }

    [FfmpegFact]
    public async Task AVfrSourceMapsExactlyAndCachedProxiesLoadWithoutEncoding()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.OracleVfr);
        var (mapping, proxy) = await kit.Proxies.EnsureAsync(asset, null, CancellationToken.None);

        // The VFR oracle's average rate equals its nominal 25 fps, so the probe does not flag it VFR; the proxy
        // keeps its timestamps anyway because -enc_time_base is always passed.
        Assert.False(asset.Info.Video!.IsVfr);
        Assert.Equal(ProxyMapping.Exact, mapping);
        Assert.NotNull(proxy);

        // A fresh catalog entry (a reopened project) gets the cached proxy back without ffmpeg encoding again.
        kit.Preview.Catalog.SetProxy(asset.Id, null);
        Assert.Null(kit.Preview.Catalog.Get(asset.Id)!.Proxy);
        var written = File.GetLastWriteTimeUtc(proxy.Path);
        var (again, loaded) = await kit.Proxies.EnsureAsync(asset, null, CancellationToken.None);
        Assert.Equal(mapping, again);
        Assert.NotNull(loaded);
        Assert.Equal(written, File.GetLastWriteTimeUtc(loaded.Path));
        Assert.Equal(proxy.Index!.Pts.ToArray(), loaded.Index!.Pts.ToArray());
        Assert.Same(loaded, kit.Preview.Catalog.Get(asset.Id)!.Proxy);

        // Deleting detaches it and removes the files.
        await kit.Proxies.DeleteAsync(asset);
        Assert.Null(kit.Preview.Catalog.Get(asset.Id)!.Proxy);
        Assert.False(File.Exists(proxy.Path));
        Assert.Null(await kit.Proxies.LoadCachedAsync(asset, CancellationToken.None));
    }

    [FfmpegTheory]
    [InlineData("intra", 100, 30, 1)]
    [InlineData("intra", 50, 30000, 1001)]
    [InlineData("lgop", 200, 30, 1)]
    [InlineData("lgop", 100, 30000, 1001)]
    [InlineData("ntsc", 50, 30, 1)]
    [InlineData("ntsc", 200, 30000, 1001)]
    [InlineData("vfr", 100, 30, 1)]
    [InlineData("vfr", 200, 30000, 1001)]
    public async Task ScrubbedFramesThroughTheProxyShowTheExpectedSourceFrame(string source, int speed, long rateNum, long rateDen)
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(PathOf(source));
        var (mapping, proxy) = await kit.Proxies.EnsureAsync(asset, null, CancellationToken.None);
        Assert.NotEqual(ProxyMapping.Rejected, mapping);
        Assert.NotNull(proxy);
        var index = kit.Preview.Catalog.Get(asset.Id)!.Original.Index;
        Assert.NotNull(index);

        var rate = new FrameRate(new Rational(rateNum, rateDen));
        var clip = V("c_o", ClipStart, ClipDuration, asset.Id, Micros(1_234_567), speed);
        var project = Project(320, 180, rate, [asset], Track("tr_v1", TrackKind.Video, [clip]));

        // The compiler really reads the proxy (and the original when proxies are off).
        var size = FrameServer.FitSize(project.Sequence.Settings, 320, 180);
        Assert.Equal(proxy.Path, Compile(kit, project, size, useProxies: true).Inputs.Single().Path);
        Assert.Equal(asset.Path, Compile(kit, project, size, useProxies: false).Inputs.Single().Path);

        var sink = new RecordingSink();
        var server = kit.Preview.Server(sink);
        var failures = new StringBuilder();
        server.RenderFailed += (id, message) => { lock (failures) { failures.Append(CultureInfo.InvariantCulture, $"request {id} failed: {message}; "); } };

        var random = new Random(HashCode.Combine(source.Length, speed, rateDen) + 4242);
        var mismatches = new StringBuilder();
        for (var i = 1; i <= Samples; i++)
        {
            var frame = random.NextInt64(0, ClipStart + ClipDuration + 5);
            server.Request(new FrameRequest(project, frame, size, true, i));
            var presented = await sink.WaitAsync(i, FrameTimeout);
            Assert.Equal(frame, presented.TimelineFrame);

            var actual = OracleReader.DecodeBgra(presented.Pixels, 320, 180, presented.Stride);
            var expected = frame < ClipStart || frame >= ClipStart + ClipDuration
                ? 0
                : TestMediaFixture.ExpectedOracleValue(SourceFrame(source, TimeMath.SourceFlicksAt(clip, frame, rate), index));
            if (actual != expected)
            {
                mismatches.Append(CultureInfo.InvariantCulture, $"frame {frame}: expected {expected}, got {actual}; ");
            }
        }

        Assert.True(failures.Length == 0, failures.ToString());
        Assert.True(mismatches.Length == 0, mismatches.ToString());
    }

    [FfmpegFact]
    public async Task AProxyDeletedByAnotherInstanceIsDetachedAndQueuedAgainWhileScrubbingContinues()
    {
        await using var kit = await CacheKit.CreateAsync();
        var asset = await kit.ImportAsync(_media.Oracle25Intra);
        var (_, proxy) = await kit.Proxies.EnsureAsync(asset, null, CancellationToken.None);
        Assert.NotNull(proxy);
        var clip = V("c_o", 0, 30, asset.Id, 0);
        var project = Project(320, 180, FrameRate.Fps25, [asset], Track("tr_v1", TrackKind.Video, [clip]));
        var (scheduler, pipeline) = kit.Pipeline();
        await using var disposeScheduler = scheduler;
        using var disposePipeline = pipeline;
        var requeued = new TaskCompletionSource<JobInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.JobChanged += info =>
        {
            if (info.Kind == JobKind.Proxy && info.MediaId == asset.Id)
            {
                requeued.TrySetResult(info);
            }
        };

        // Another instance's "Clear cache" removes the proxy files behind this catalog's back (TECH_BRIEF 6.1).
        File.Delete(proxy.Path);

        var sink = new RecordingSink();
        var server = kit.Preview.Server(sink);
        var failures = new StringBuilder();
        server.RenderFailed += (id, message) => { lock (failures) { failures.Append(message); } };
        var size = FrameServer.FitSize(project.Sequence.Settings, 320, 180);
        server.Request(new FrameRequest(project, 12, size, true, 1));
        var presented = await sink.WaitAsync(1, FrameTimeout);

        Assert.True(failures.Length == 0, failures.ToString());
        Assert.Equal(TestMediaFixture.ExpectedOracleValue(12), OracleReader.DecodeBgra(presented.Pixels, 320, 180, presented.Stride));
        Assert.Null(kit.Preview.Catalog.Get(asset.Id)!.Proxy);
        Assert.Equal(JobKind.Proxy, (await requeued.Task.WaitAsync(TimeSpan.FromSeconds(10))).Kind);
    }

    private static CompiledRender Compile(CacheKit kit, Project project, PreviewSize size, bool useProxies) =>
        TimelineCompiler.Compile(
            project,
            kit.Preview.Catalog,
            new RenderRequest(
                10,
                11,
                size.Width,
                size.Height,
                RenderPurpose.PreviewFrame,
                IncludeVideo: true,
                IncludeAudio: false,
                UseProxies: useProxies,
                AudioLayout.Mixed,
                FrameStep: 1,
                OutputTail.Preview,
                LimitPeaks: false));

    private static long SourceFrame(string source, long sourceFlicks, FrameIndex? index) => source switch
    {
        // Frame k of a 25 fps oracle sits exactly at k/25 s; sample-and-hold shows floor(s * 25).
        "intra" or "lgop" => (long)TimeMath.FloorDiv((Int128)sourceFlicks * 25, TimeMath.FlicksPerSecond),

        // The 30000/1001 Matroska oracle (1 ms ticks) and the VFR oracle: the largest stored pts at or before s.
        _ => index!.FloorOrdinal(sourceFlicks),
    };

    private string PathOf(string source) => source switch
    {
        "intra" => _media.Oracle25Intra,
        "lgop" => _media.Oracle25LongGop,
        "ntsc" => _media.Oracle2997,
        _ => _media.OracleVfr,
    };
}
