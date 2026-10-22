using System.Buffers;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Services;

namespace Cutlass.Media.Preview;

/// <summary>
/// Renders single timeline frames on request for agents (AGENT_INTEGRATION 2.6 and 3.8). It compiles exactly as the
/// <see cref="FrameServer"/> does (<see cref="PreviewRender.Frame"/>, the preview tail and the same
/// <see cref="FrameCache"/> key), so it shares the cache with the viewer, but it never touches the viewer's
/// latest-wins channel: every call runs its own process, at most <see cref="MaxConcurrent"/> at once. Thread-safe.
/// </summary>
public sealed class StillRenderer
{
    /// <summary>The most renders that run at once; further calls wait.</summary>
    public const int MaxConcurrent = 2;

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly FfmpegCapabilities _capabilities;
    private readonly WorkDirectory _work;
    private readonly IMediaCatalog _catalog;
    private readonly FrameCache _cache;
    private readonly PreviewFontResolver _fonts;
    private readonly SemaphoreSlim _slots = new(MaxConcurrent, MaxConcurrent);
    private readonly SemaphoreSlim _fontGate = new(1, 1);

    /// <summary>Creates the renderer.</summary>
    /// <param name="runner">Starts ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="capabilities">What the build can do (script syntax, drawtext).</param>
    /// <param name="work">The work directory every render runs in.</param>
    /// <param name="catalog">Resolves the media of a project.</param>
    /// <param name="fonts">Places title fonts into the work directory.</param>
    /// <param name="cache">The frame cache shared with the frame server and playback.</param>
    public StillRenderer(
        ProcessRunner runner,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        WorkDirectory work,
        IMediaCatalog catalog,
        FontLocator fonts,
        FrameCache cache)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(fonts);
        ArgumentNullException.ThrowIfNull(cache);
        _runner = runner;
        _toolset = toolset;
        _capabilities = capabilities;
        _work = work;
        _catalog = catalog;
        _cache = cache;
        _fonts = new PreviewFontResolver(catalog, fonts, work);
    }

    /// <summary>The shared frame cache.</summary>
    public FrameCache Cache => _cache;

    /// <summary>
    /// Renders one timeline frame of a snapshot as BGRA. A negative frame renders frame 0. The caller must
    /// <see cref="VideoFrame.Return"/> the frame when done.
    /// </summary>
    /// <param name="snapshot">The immutable project.</param>
    /// <param name="frame">The timeline frame.</param>
    /// <param name="size">The output size (even dimensions).</param>
    /// <param name="useProxies">True to read video from proxies where one exists.</param>
    /// <param name="ct">Cancels the render (kills the process).</param>
    /// <returns>The frame.</returns>
    /// <exception cref="FfmpegException">When ffmpeg fails.</exception>
    /// <exception cref="InvalidOperationException">When titles need drawtext and the build lacks it.</exception>
    public async Task<VideoFrame> RenderAsync(Project snapshot, long frame, PreviewSize size, bool useProxies, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(size);
        if (!size.IsValid)
        {
            throw new ArgumentException($"The preview size {size.Width}x{size.Height} must be positive and even.", nameof(size));
        }

        await _slots.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                return await RenderCoreAsync(snapshot, frame, size, useProxies, ct).ConfigureAwait(false);
            }
            catch (FfmpegException) when (useProxies && _catalog is MediaCatalog media && media.DetachMissingProxies().Count > 0)
            {
                // A proxy deleted by another instance: render once more from the originals, as the frame server does.
                return await RenderCoreAsync(snapshot, frame, size, useProxies, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<VideoFrame> RenderCoreAsync(Project project, long requested, PreviewSize size, bool useProxies, CancellationToken ct)
    {
        var frame = Math.Max(0, requested);
        IMediaCatalog catalog;
        await _fontGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            catalog = await _fonts.ResolveAsync(project, ct).ConfigureAwait(false);
        }
        finally
        {
            _fontGate.Release();
        }

        var render = TimelineCompiler.Compile(project, catalog, PreviewRender.Frame(frame, size, useProxies));
        var fingerprints = PreviewRender.Fingerprints(project, _catalog);
        var key = FrameCache.Key(render, size, fingerprints);
        if (_cache.TryGet(key, out var cached))
        {
            return new VideoFrame(cached, size.Width, size.Height, size.Width * 4, requested, 0, pooled: false);
        }

        var length = checked((int)size.FrameBytes);
        var spec = PreviewRender.Spec(render, RenderPurpose.PreviewFrame, video: true, frames: 1, _toolset, _capabilities, _work);
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            await ReadFrameAsync(spec, buffer, length, ct).ConfigureAwait(false);
            _cache.Add(key, buffer.AsSpan(0, length).ToArray());
            return new VideoFrame(buffer, size.Width, size.Height, size.Width * 4, requested, 0, pooled: true);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    // The same read as FrameServer.ReadFrameAsync: the timeout kills the process, and a short exit wait follows.
    private async Task ReadFrameAsync(ProcessSpec spec, byte[] buffer, int length, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(FrameServer.ScrubTimeout);
        await using var process = _runner.Start(spec);
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
                        $"ffmpeg did not deliver the frame within {FrameServer.ScrubTimeout.TotalSeconds:0} s and was stopped.",
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

        await Task.WhenAny(process.Exited, Task.Delay(TimeSpan.FromSeconds(2), token)).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
    }
}
