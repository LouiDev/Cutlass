using System.Buffers;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Core.Model;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Preview;

/// <summary>
/// Streams the mixed audio of a timeline range as s16le, 48 kHz stereo (AGENT_INTEGRATION 2.6) through the playback
/// audio path: <see cref="PreviewRender.Audio"/> with <see cref="AudioLayout.Mixed"/> and the raw audio arguments.
/// Track mute, solo and gain come from the snapshot. Thread-safe; every call runs its own process.
/// </summary>
public sealed class RangeAudioReader
{
    /// <summary>The bytes of one stereo s16le sample frame.</summary>
    public const int BytesPerSampleFrame = 4;

    private const int ChunkBytes = 64 * 1024;

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly FfmpegCapabilities _capabilities;
    private readonly WorkDirectory _work;
    private readonly IMediaCatalog _catalog;

    /// <summary>Creates the reader.</summary>
    /// <param name="runner">Starts ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="capabilities">What the build can do (script syntax).</param>
    /// <param name="work">The work directory every process runs in.</param>
    /// <param name="catalog">Resolves the media of a project.</param>
    public RangeAudioReader(ProcessRunner runner, FfmpegToolset toolset, FfmpegCapabilities capabilities, WorkDirectory work, IMediaCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(catalog);
        _runner = runner;
        _toolset = toolset;
        _capabilities = capabilities;
        _work = work;
        _catalog = catalog;
    }

    /// <summary>
    /// Renders <c>[startFrame, endFrame)</c> of the snapshot's mix and hands the PCM to <paramref name="onPcm"/> in
    /// chunks, in order. The chunks always hold whole stereo sample frames except possibly at the very end.
    /// </summary>
    /// <param name="snapshot">The immutable project.</param>
    /// <param name="startFrame">The first timeline frame.</param>
    /// <param name="endFrame">The end frame (exclusive).</param>
    /// <param name="onPcm">Receives the chunks; the memory is only valid during the call.</param>
    /// <param name="ct">Cancels the read (kills the process).</param>
    /// <returns>The number of bytes delivered.</returns>
    /// <exception cref="FfmpegException">When ffmpeg fails.</exception>
    public async Task<long> ReadAsync(Project snapshot, long startFrame, long endFrame, Func<ReadOnlyMemory<byte>, ValueTask> onPcm, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(onPcm);
        var start = Math.Max(0, startFrame);
        if (endFrame <= start)
        {
            return 0;
        }

        // The canvas only has to be valid for audio; use the sequence size made even.
        var settings = snapshot.Sequence.Settings;
        var size = new PreviewSize(Math.Max(2, settings.Width / 2 * 2), Math.Max(2, settings.Height / 2 * 2));
        var render = TimelineCompiler.Compile(snapshot, _catalog, PreviewRender.Audio(start, endFrame, size, useProxies: false, AudioLayout.Mixed));
        var spec = PreviewRender.Spec(render, RenderPurpose.PreviewAudio, video: false, frames: null, _toolset, _capabilities, _work);
        var expected = render.SampleCount * BytesPerSampleFrame;
        await using var process = _runner.Start(spec);
        var registration = ct.Register(() => _ = process.KillAsync());
        await using (registration.ConfigureAwait(false))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(ChunkBytes);
            long total = 0;
            try
            {
                var filled = 0;
                while (true)
                {
                    int read;
                    try
                    {
                        read = await process.Stdout.ReadAsync(buffer.AsMemory(filled, ChunkBytes - filled), ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        ct.ThrowIfCancellationRequested();
                        throw;
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    filled += read;
                    var whole = filled - (filled % BytesPerSampleFrame);
                    if (filled == ChunkBytes || whole > 0)
                    {
                        await onPcm(buffer.AsMemory(0, whole)).ConfigureAwait(false);
                        total += whole;
                        var rest = filled - whole;
                        if (rest > 0)
                        {
                            buffer.AsSpan(whole, rest).CopyTo(buffer);
                        }

                        filled = rest;
                    }
                }

                if (filled > 0)
                {
                    await onPcm(buffer.AsMemory(0, filled)).ConfigureAwait(false);
                    total += filled;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            ct.ThrowIfCancellationRequested();
            var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            if (exitCode != 0 || (total == 0 && expected > 0))
            {
                throw new FfmpegException(
                    exitCode != 0 ? $"ffmpeg exited with code {exitCode} while reading audio." : "ffmpeg delivered no audio.",
                    exitCode,
                    process.StderrTail(ProcessResult.ExceptionTailLines),
                    process.DisplayCommand);
            }

            return total;
        }
    }
}
