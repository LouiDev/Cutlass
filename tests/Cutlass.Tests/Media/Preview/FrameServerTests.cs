using System.Collections.Concurrent;
using System.Collections.Immutable;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Parsing;
using Cutlass.Media.Platform;
using Cutlass.Media.Preview;
using Cutlass.Tests.Compiler;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Media.Preview;

/// <summary>
/// Pure frame server logic: <see cref="FrameServer.FitSize"/>, the cache key, and the request channel semantics with
/// an ffmpeg that cannot start (every render fails, so the reported ids show which requests the worker took).
/// </summary>
public sealed class FrameServerTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "cutlass-fs-" + Guid.NewGuid().ToString("N")[..12]);

    [Theory]
    [InlineData(1920, 1080, 960, 540, 960, 540)]
    [InlineData(1920, 1080, 4000, 4000, 1920, 1080)]
    [InlineData(1920, 1080, 1000, 1000, 1000, 562)]
    [InlineData(1920, 1080, 1001, 2000, 1000, 562)]
    [InlineData(1920, 1080, 2000, 301, 534, 300)]
    [InlineData(1080, 1920, 800, 450, 252, 448)]
    [InlineData(1280, 720, 1, 1, 2, 2)]
    [InlineData(320, 180, 320, 180, 320, 180)]
    public void FitSizeIsEvenAspectPreservingAndNeverUpscales(int seqW, int seqH, int maxW, int maxH, int expectedW, int expectedH)
    {
        var size = FrameServer.FitSize(new SequenceSettings(seqW, seqH, FrameRate.Fps25), maxW, maxH);

        Assert.Equal(new PreviewSize(expectedW, expectedH), size);
        Assert.True(size.IsValid);
    }

    [Fact]
    public void FitSizeStaysInsideTheBoxForManySizes()
    {
        var settings = new SequenceSettings(1920, 1080, FrameRate.Fps30);
        for (var w = 2; w < 2100; w += 37)
        {
            for (var h = 2; h < 1200; h += 41)
            {
                var size = FrameServer.FitSize(settings, w, h);
                Assert.True(size.IsValid);
                Assert.True(size.Width <= Math.Max(2, w) && size.Height <= Math.Max(2, h), $"{w}x{h} -> {size}");
                Assert.True(Math.Abs(((double)size.Width / size.Height) - (16.0 / 9.0)) < 0.2 || size.Height <= 4, $"{w}x{h} -> {size}");
            }
        }
    }

    [Fact]
    public void PreviewSizeReportsValidityAndFrameBytes()
    {
        Assert.True(new PreviewSize(960, 540).IsValid);
        Assert.False(new PreviewSize(961, 540).IsValid);
        Assert.False(new PreviewSize(0, 540).IsValid);
        Assert.Equal(960L * 540 * 4, new PreviewSize(960, 540).FrameBytes);
    }

    [Fact]
    public void TheCacheKeyIsDeterministicAndSensitiveToGraphSizeAndContent()
    {
        var project = new ProjectBuilder().With(ProjectBuilder.V1, TestMedia.V("c_1", 0, 50)).Build();
        var catalog = TestCatalog.For(project);
        var render = TimelineCompiler.Compile(project, catalog, CompilerKit.PreviewFrame(10, 11, 960, 540));
        var other = TimelineCompiler.Compile(project, catalog, CompilerKit.PreviewFrame(11, 12, 960, 540));
        var fingerprints = new Dictionary<string, string> { [TestMedia.All[0].Path] = "sha256:aa" };
        var changed = new Dictionary<string, string> { [TestMedia.All[0].Path] = "sha256:bb" };

        var key = FrameServer.CacheKey(render, new PreviewSize(960, 540), fingerprints);

        Assert.Matches("^[0-9a-f]{64}$", key);
        Assert.Equal(key, FrameServer.CacheKey(render, new PreviewSize(960, 540), fingerprints));
        Assert.NotEqual(key, FrameServer.CacheKey(other, new PreviewSize(960, 540), fingerprints));
        Assert.NotEqual(key, FrameServer.CacheKey(render, new PreviewSize(480, 270), fingerprints));
        Assert.NotEqual(key, FrameServer.CacheKey(render, new PreviewSize(960, 540), changed));
    }

    [Fact]
    public async Task RequestNeverThrowsAndFailuresAreReportedWithTheirId()
    {
        var failures = new ConcurrentQueue<(long Id, string Message)>();
        await using var server = CreateServer(new NullSink(), out _);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RenderFailed += (id, message) =>
        {
            failures.Enqueue((id, message));
            if (id == 3)
            {
                done.TrySetResult();
            }
        };
        var project = new ProjectBuilder().Build();

        server.Request(null!);
        server.Request(new FrameRequest(project, 0, new PreviewSize(321, 180), false, 1));
        await WaitUntilAsync(() => failures.Any(f => f.Id == 1));
        server.Request(new FrameRequest(project, 0, new PreviewSize(320, 180), false, 2));
        await WaitUntilAsync(() => failures.Any(f => f.Id == 2));
        server.Request(new FrameRequest(project, -5, new PreviewSize(320, 180), false, 3));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var list = failures.ToList();
        Assert.Contains("even", list.Single(f => f.Id == 1).Message, StringComparison.Ordinal);
        Assert.Contains("Could not start", list.Single(f => f.Id == 2).Message, StringComparison.Ordinal);
        Assert.Contains("Could not start", list.Single(f => f.Id == 3).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLatestRequestWinsAndIsHandledLast()
    {
        var handled = new ConcurrentQueue<long>();
        var gate = new ManualResetEventSlim(false);
        await using var server = CreateServer(new NullSink(), out _);
        var last = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.RenderFailed += (id, _) =>
        {
            // Hold the worker on the first request so the flood queues up behind it.
            gate.Wait(TimeSpan.FromSeconds(30));
            handled.Enqueue(id);
            if (id == 20)
            {
                last.TrySetResult();
            }
        };
        var project = new ProjectBuilder().Build();

        for (var id = 1; id <= 20; id++)
        {
            server.Request(new FrameRequest(project, id, new PreviewSize(320, 180), false, id));
        }

        gate.Set();
        await last.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await Task.Delay(200);

        var ids = handled.ToList();
        Assert.Equal(20, ids[^1]);
        Assert.Equal(ids.OrderBy(i => i), ids);
        Assert.InRange(ids.Count, 1, 2);
    }

    [Fact]
    public async Task RequestsAfterDisposeAreIgnored()
    {
        var failures = new ConcurrentQueue<long>();
        var server = CreateServer(new NullSink(), out _);
        server.RenderFailed += (id, _) => failures.Enqueue(id);
        await server.DisposeAsync();
        await server.DisposeAsync();

        server.Request(new FrameRequest(new ProjectBuilder().Build(), 0, new PreviewSize(320, 180), false, 1));
        await Task.Delay(200);

        Assert.Empty(failures);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Never created.
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out.");
            await Task.Delay(10);
        }
    }

    private FrameServer CreateServer(IVideoFrameSink sink, out WorkDirectory work)
    {
        work = new WorkDirectory(Path.Combine(_temp, "work"));
        var registry = new ChildProcessRegistry(recordDirectory: null);
        var runner = new ProcessRunner(registry, job: null);
        var missing = Path.Combine(_temp, "no-such-ffmpeg", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        var toolset = new FfmpegToolset(missing, missing + "probe", new FfmpegVersion("test", 12, 0));
        var capabilities = new FfmpegCapabilities(
            "test",
            12,
            0,
            FilterScriptSyntax.Slash,
            ImmutableSortedSet.Create(StringComparer.Ordinal, [.. FfmpegCapabilities.RequiredFilters, "drawtext"]),
            ImmutableSortedSet<string>.Empty.WithComparer(StringComparer.Ordinal));
        var fonts = new FontLocator(AppContext.BaseDirectory, work, runner);
        return new FrameServer(runner, toolset, capabilities, work, new EmptyCatalog(), fonts, sink);
    }

    private sealed class NullSink : IVideoFrameSink
    {
        public void Present(VideoFrame frame) => frame.Return();
    }

    private sealed class EmptyCatalog : IMediaCatalog
    {
        public MediaRenderInfo? Get(string mediaId) => null;
    }
}
