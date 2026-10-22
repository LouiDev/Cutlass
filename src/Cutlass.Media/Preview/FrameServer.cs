using System.Buffers;
using System.Threading.Channels;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Services;

namespace Cutlass.Media.Preview;

/// <summary>
/// Renders single scrub and step frames (TECH_BRIEF 8.1). <see cref="Request"/> writes into a channel bounded to one
/// item with <c>DropOldest</c>, so the latest request wins. One worker compiles <c>[f, f+1)</c> with
/// <see cref="RenderPurpose.PreviewFrame"/> and the preview tail through the same compiler path export uses, looks
/// the frame up in the shared <see cref="FrameCache"/>, and otherwise writes the aux files, runs
/// <c>ffmpeg -hide_banner -nostdin -v error &lt;inputs&gt; -filter_complex &lt;graph&gt; -map [vout] -frames:v 1
/// -fps_mode passthrough -f rawvideo -pix_fmt bgra pipe:1</c>, reads exactly <c>W*H*4</c> bytes and presents them
/// through the <see cref="IVideoFrameSink"/>. A running scrub is never killed for a newer request; only dispose (or a
/// hung process) kills it. With <see cref="PrefetchEnabled"/>, 150 ms of idle after a frame renders
/// <c>[f-6, f+18)</c> as one PreviewVideo window into the cache; a new request kills a prefetch.
/// </summary>
public sealed class FrameServer : IAsyncDisposable
{
    /// <summary>A scrub process that has not delivered its frame after this long is killed and reported.</summary>
    public static readonly TimeSpan ScrubTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long the server must be idle after a frame before it prefetches the neighbours (150 ms).</summary>
    public static readonly TimeSpan PrefetchIdle = TimeSpan.FromMilliseconds(150);

    /// <summary>How many frames before the requested one the prefetch window starts.</summary>
    public const int PrefetchBefore = 6;

    /// <summary>How many frames after the requested one the prefetch window ends (exclusive).</summary>
    public const int PrefetchAfter = 18;

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly FfmpegCapabilities _capabilities;
    private readonly WorkDirectory _work;
    private readonly IMediaCatalog _catalog;
    private readonly IVideoFrameSink _sink;
    private readonly FrameCache _cache;
    private readonly PreviewFontResolver _fonts;
    private readonly Channel<FrameRequest> _requests;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _worker;
    private int _disposed;
    private int _prefetchEnabled;
    private long _prefetchRuns;

    /// <summary>Creates the server with a private 512 MB <see cref="FrameCache"/> and starts its worker.</summary>
    /// <param name="runner">Starts ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="capabilities">What the build can do (script syntax, drawtext).</param>
    /// <param name="work">The work directory every scrub runs in.</param>
    /// <param name="catalog">Resolves the media of a project.</param>
    /// <param name="fonts">Places title fonts into the work directory.</param>
    /// <param name="sink">Receives the frames.</param>
    public FrameServer(
        ProcessRunner runner,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        WorkDirectory work,
        IMediaCatalog catalog,
        FontLocator fonts,
        IVideoFrameSink sink)
        : this(runner, toolset, capabilities, work, catalog, fonts, sink, new FrameCache())
    {
    }

    /// <summary>Creates the server on a shared <see cref="FrameCache"/> and starts its worker.</summary>
    /// <param name="runner">Starts ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="capabilities">What the build can do (script syntax, drawtext).</param>
    /// <param name="work">The work directory every scrub runs in.</param>
    /// <param name="catalog">Resolves the media of a project.</param>
    /// <param name="fonts">Places title fonts into the work directory.</param>
    /// <param name="sink">Receives the frames.</param>
    /// <param name="cache">The frame cache shared with playback.</param>
    public FrameServer(
        ProcessRunner runner,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        WorkDirectory work,
        IMediaCatalog catalog,
        FontLocator fonts,
        IVideoFrameSink sink,
        FrameCache cache)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(cache);
        _runner = runner;
        _toolset = toolset;
        _capabilities = capabilities;
        _work = work;
        _catalog = catalog;
        _sink = sink;
        _cache = cache;
        _fonts = new PreviewFontResolver(catalog, fonts, work);
        _requests = Channel.CreateBounded<FrameRequest>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        _worker = Task.Run(RunAsync);
    }

    /// <summary>
    /// Raised on the worker thread when a request could not be rendered: <c>(requestId, message)</c>. Handlers must
    /// not block; exceptions they throw are swallowed.
    /// </summary>
    public event Action<long, string>? RenderFailed;

    /// <summary>The number of frames in the frame cache (shared with playback when one was passed in).</summary>
    public int CachedFrameCount => _cache.Count;

    /// <summary>The frame cache.</summary>
    public FrameCache Cache => _cache;

    /// <summary>
    /// True to prefetch <c>[f-6, f+18)</c> after 150 ms of idle (TECH_BRIEF 8.1 step 5). The viewer turns it on while
    /// paused and off while playing. Off by default.
    /// </summary>
    public bool PrefetchEnabled
    {
        get => Volatile.Read(ref _prefetchEnabled) != 0;
        set => Volatile.Write(ref _prefetchEnabled, value ? 1 : 0);
    }

    /// <summary>How many prefetch windows were started (diagnostics and tests).</summary>
    public long PrefetchRuns => Interlocked.Read(ref _prefetchRuns);

    /// <summary>
    /// The largest even, aspect preserving size of the sequence canvas that fits into
    /// <paramref name="maxWidth"/> x <paramref name="maxHeight"/>. Never larger than the sequence and never smaller
    /// than 2 x 2.
    /// </summary>
    /// <param name="s">The sequence settings.</param>
    /// <param name="maxWidth">The available width in pixels.</param>
    /// <param name="maxHeight">The available height in pixels.</param>
    /// <returns>The preview size.</returns>
    public static PreviewSize FitSize(SequenceSettings s, int maxWidth, int maxHeight)
    {
        ArgumentNullException.ThrowIfNull(s);
        var seqW = Math.Max(2, s.Width);
        var seqH = Math.Max(2, s.Height);

        // Never upscale: a preview larger than the canvas costs time and shows nothing more.
        var boxW = Math.Clamp(maxWidth, 2, seqW);
        var boxH = Math.Clamp(maxHeight, 2, seqH);
        var scale = Math.Min((double)boxW / seqW, (double)boxH / seqH);

        var w = EvenFloor(seqW * scale);
        var h = EvenRound((double)w * seqH / seqW);
        if (h > boxH)
        {
            h = EvenFloor(boxH);
            w = Math.Min(EvenRound((double)h * seqW / seqH), EvenFloor(boxW));
        }

        return new PreviewSize(Math.Max(2, w), Math.Max(2, h));
    }

    /// <summary>
    /// Queues a frame. The newest request replaces one that is still waiting; a request that is already rendering
    /// finishes. Never throws and never blocks; requests after dispose are ignored.
    /// </summary>
    /// <param name="request">The request.</param>
    public void Request(FrameRequest request)
    {
        if (request is null || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        _requests.Writer.TryWrite(request);
    }

    /// <summary>Stops the worker, kills a running scrub or prefetch process and waits for the worker to end.</summary>
    /// <returns>A task that completes when the worker has stopped.</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _requests.Writer.TryComplete();
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The worker stopped.
        }

        _stop.Dispose();
    }

    /// <summary>The cache key of a compiled frame; see <see cref="FrameCache.Key"/>.</summary>
    /// <param name="render">The compiled frame.</param>
    /// <param name="size">The output size.</param>
    /// <param name="fingerprints">Media path to fingerprint (paths without one count as unknown).</param>
    /// <returns>The lowercase hex key.</returns>
    internal static string CacheKey(CompiledRender render, PreviewSize size, IReadOnlyDictionary<string, string> fingerprints) =>
        FrameCache.Key(render, size, fingerprints);

    private static int EvenFloor(double value) => (int)Math.Floor(value / 2) * 2;

    private static int EvenRound(double value) => (int)Math.Round(value / 2, MidpointRounding.AwayFromZero) * 2;

    private async Task RunAsync()
    {
        var token = _stop.Token;
        try
        {
            while (await _requests.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                if (!_requests.Reader.TryRead(out var request))
                {
                    continue;
                }

                RenderedContext rendered;
                try
                {
                    rendered = await RenderWithRecoveryAsync(request, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ReportFailure(request.RequestId, PreviewRender.Describe(ex));
                    continue;
                }

                if (PrefetchEnabled)
                {
                    try
                    {
                        await PrefetchAfterIdleAsync(rendered, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex) when (ex is FfmpegException or IOException or InvalidOperationException or ArgumentException)
                    {
                        // A failed prefetch only costs the cache entries; the next scrub renders on its own.
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed while waiting for a request.
        }
    }

    // A proxy deleted by another instance (TECH_BRIEF 6.1) fails the render; detach the missing proxies and render
    // once more from the originals.
    private async Task<RenderedContext> RenderWithRecoveryAsync(FrameRequest request, CancellationToken token)
    {
        try
        {
            return await RenderAsync(request, token).ConfigureAwait(false);
        }
        catch (FfmpegException) when (request.UseProxies && _catalog is MediaCatalog media && media.DetachMissingProxies().Count > 0)
        {
            return await RenderAsync(request, token).ConfigureAwait(false);
        }
    }

    private void ReportFailure(long requestId, string message)
    {
        try
        {
            RenderFailed?.Invoke(requestId, message);
        }
        catch (Exception)
        {
            // A failing handler must not stop the worker.
        }
    }

    private async Task<RenderedContext> RenderAsync(FrameRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request.Snapshot);
        var size = request.Size ?? throw new ArgumentException("The frame request has no size.", nameof(request));
        if (!size.IsValid)
        {
            throw new ArgumentException($"The preview size {size.Width}x{size.Height} must be positive and even.", nameof(request));
        }

        // A negative frame (a playhead before the start) shows frame 0, the same clamp the timeline applies.
        var frame = Math.Max(0, request.Frame);
        var project = request.Snapshot;
        var catalog = await _fonts.ResolveAsync(project, token).ConfigureAwait(false);
        var render = TimelineCompiler.Compile(project, catalog, PreviewRender.Frame(frame, size, request.UseProxies));
        var fingerprints = PreviewRender.Fingerprints(project, _catalog);
        var context = new RenderedContext(project, catalog, fingerprints, frame, size, request.UseProxies);

        var key = FrameCache.Key(render, size, fingerprints);
        var length = checked((int)size.FrameBytes);
        if (_cache.TryGet(key, out var cached))
        {
            _sink.Present(new VideoFrame(cached, size.Width, size.Height, size.Width * 4, request.Frame, request.RequestId, pooled: false));
            return context;
        }

        var spec = PreviewRender.Spec(render, RenderPurpose.PreviewFrame, video: true, frames: 1, _toolset, _capabilities, _work);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        var handedOff = false;
        try
        {
            await ReadFrameAsync(spec, buffer, length, token).ConfigureAwait(false);
            _cache.Add(key, buffer.AsSpan(0, length).ToArray());
            var result = new VideoFrame(buffer, size.Width, size.Height, size.Width * 4, request.Frame, request.RequestId, pooled: true);
            handedOff = true;
            _sink.Present(result);
        }
        finally
        {
            if (!handedOff)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        return context;
    }

    private async Task ReadFrameAsync(ProcessSpec spec, byte[] buffer, int length, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(ScrubTimeout);
        await using var process = _runner.Start(spec);

        // Pipe reads do not observe cancellation on every platform; killing the process ends the read instead.
        var registration = timeout.Token.Register(() => _ = process.KillAsync());
        await using (registration.ConfigureAwait(false))
        {
            try
            {
                await process.Stdout.ReadExactlyAsync(buffer.AsMemory(0, length), timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException or OperationCanceledException)
            {
                token.ThrowIfCancellationRequested();
                if (timeout.IsCancellationRequested)
                {
                    await process.KillAsync().ConfigureAwait(false);
                    throw new FfmpegException(
                        $"ffmpeg did not deliver the frame within {ScrubTimeout.TotalSeconds:0} s and was stopped.",
                        FfmpegException.NoExitCode,
                        process.StderrTail(ProcessResult.ExceptionTailLines),
                        process.DisplayCommand,
                        ex);
                }

                var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                throw new FfmpegException(
                    exitCode != 0
                        ? $"ffmpeg exited with code {exitCode} before it delivered the frame."
                        : "ffmpeg ended before it delivered the frame.",
                    exitCode,
                    process.StderrTail(ProcessResult.ExceptionTailLines),
                    process.DisplayCommand,
                    ex);
            }
        }

        // -frames:v 1 makes ffmpeg exit right after the frame; give it a moment so dispose rarely has to kill it.
        await Task.WhenAny(process.Exited, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }

    // TECH_BRIEF 8.1 step 5: after 150 ms without a new request, render [f-6, f+18) as one PreviewVideo window and cache
    // each frame under its own single-frame key (compiled as text only). A new request (or dispose) kills it.
    private async Task PrefetchAfterIdleAsync(RenderedContext context, CancellationToken token)
    {
        using var watch = CancellationTokenSource.CreateLinkedTokenSource(token);
        var arrival = _requests.Reader.WaitToReadAsync(watch.Token).AsTask();
        try
        {
            var idle = Task.Delay(PrefetchIdle, token);
            if (await Task.WhenAny(arrival, idle).ConfigureAwait(false) == arrival)
            {
                return;
            }

            token.ThrowIfCancellationRequested();
            var size = context.Size;
            var start = Math.Max(0, context.Frame - PrefetchBefore);

            // Frames past the end of the timeline are black and rarely visited; the window stops at the end.
            var end = Math.Max(context.Frame + 1, Math.Min(context.Frame + PrefetchAfter, TimelineQuery.Duration(context.Project)));
            var keys = new string[end - start];
            var missing = false;
            for (var f = start; f < end; f++)
            {
                keys[f - start] = PreviewRender.FrameKey(context.Project, context.Catalog, context.Fingerprints, f, size, context.UseProxies);
                missing |= !_cache.Contains(keys[f - start]);
            }

            if (!missing || arrival.IsCompleted)
            {
                return;
            }

            var render = TimelineCompiler.Compile(context.Project, context.Catalog, PreviewRender.Video(start, end, size, context.UseProxies, 1));
            var spec = PreviewRender.Spec(render, RenderPurpose.PreviewVideo, video: true, frames: null, _toolset, _capabilities, _work);
            Interlocked.Increment(ref _prefetchRuns);
            await using var process = _runner.Start(spec);
            var read = ReadPrefetchAsync(process, keys, checked((int)size.FrameBytes), render.FrameCount);
            if (await Task.WhenAny(read, arrival).ConfigureAwait(false) != read)
            {
                await process.KillAsync().ConfigureAwait(false);
            }

            try
            {
                await read.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is EndOfStreamException or IOException or ObjectDisposedException)
            {
                // Killed for a newer request, or ffmpeg ended early: the frames read so far stay cached.
            }
        }
        finally
        {
            await watch.CancelAsync().ConfigureAwait(false);
            try
            {
                await arrival.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ChannelClosedException)
            {
                // The watch ended with the prefetch.
            }
        }
    }

    private async Task ReadPrefetchAsync(RunningProcess process, string[] keys, int length, long frameCount)
    {
        var count = Math.Min(frameCount, keys.Length);
        for (var k = 0; k < count; k++)
        {
            var pixels = new byte[length];
            await process.Stdout.ReadExactlyAsync(pixels.AsMemory(), CancellationToken.None).ConfigureAwait(false);
            if (!_cache.Contains(keys[k]))
            {
                _cache.Add(keys[k], pixels);
            }
        }
    }

    private sealed record RenderedContext(
        Project Project,
        IMediaCatalog Catalog,
        IReadOnlyDictionary<string, string> Fingerprints,
        long Frame,
        PreviewSize Size,
        bool UseProxies);
}
