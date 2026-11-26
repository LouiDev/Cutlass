using System.Buffers;
using System.Threading.Channels;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Preview.Playback;

/// <summary>
/// Reads the rawvideo BGRA output of every playback window in order (TECH_BRIEF 8.2) into a queue of
/// <see cref="QueueCapacity"/> pooled frames. While the queue is full it stops reading, and the pipe throttles ffmpeg
/// (no <c>-re</c>). Output frame <c>k</c> of a window is labelled with timeline frame <c>start + k * Speed</c>. When
/// <see cref="PlaybackPipeline.PrefetchOutputFrames"/> frames are left in a window it asks the pipeline to start the
/// next one, whose output it reads after EOF of the current window.
/// </summary>
internal sealed class VideoWindowReader
{
    /// <summary>The queue length (TECH_BRIEF 4 and 8.2).</summary>
    public const int QueueCapacity = 8;

    private readonly PlaybackPipeline _pipeline;
    private readonly PreviewSize _size;
    private readonly Channel<VideoFrame> _frames = Channel.CreateBounded<VideoFrame>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = true,
    });

    private readonly TaskCompletionSource _firstFrame = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _framesRead;

    /// <summary>Creates the reader.</summary>
    /// <param name="pipeline">Starts the window processes.</param>
    /// <param name="size">The frame size.</param>
    public VideoWindowReader(PlaybackPipeline pipeline, PreviewSize size)
    {
        _pipeline = pipeline;
        _size = size;
    }

    /// <summary>The decoded frames, oldest first. The consumer owns every frame it reads and must return it.</summary>
    public ChannelReader<VideoFrame> Frames => _frames.Reader;

    /// <summary>Completes when the first frame is queued (or faults when the first window fails).</summary>
    public Task FirstFrame => _firstFrame.Task;

    /// <summary>How many frames were read from ffmpeg.</summary>
    public long FramesRead => Interlocked.Read(ref _framesRead);

    /// <summary>
    /// Reads every window until the end, then completes <see cref="Frames"/>. Cancellation (with the processes killed
    /// by the pipeline) ends it quietly.
    /// </summary>
    /// <param name="token">Stops reading.</param>
    /// <returns>A task that completes when reading ended; it faults on an ffmpeg failure.</returns>
    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            for (var index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                if (_pipeline.Video(index) is not { } current)
                {
                    break;
                }

                await ReadWindowAsync(index, current.Window, current.Process, current.Frames, token).ConfigureAwait(false);
                await _pipeline.ReleaseAsync(current.Process).ConfigureAwait(false);
            }

            _frames.Writer.TryComplete();
            _firstFrame.TrySetResult();
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Stopped: the killed processes end the reads with EOF or IO errors.
            _frames.Writer.TryComplete();
            _firstFrame.TrySetCanceled(token);
        }
        catch (Exception ex)
        {
            _frames.Writer.TryComplete(ex);
            _firstFrame.TrySetException(ex);
            throw;
        }
    }

    private async Task ReadWindowAsync(int index, PlaybackWindow window, RunningProcess process, long frames, CancellationToken token)
    {
        var length = checked((int)_size.FrameBytes);
        for (long k = 0; k < frames; k++)
        {
            if (frames - k <= _pipeline.PrefetchOutputFrames)
            {
                _pipeline.EnsureStarted(index + 1);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(length);
            var queued = false;
            try
            {
                try
                {
                    await process.Stdout.ReadExactlyAsync(buffer.AsMemory(0, length), token).ConfigureAwait(false);
                }
                catch (EndOfStreamException ex)
                {
                    token.ThrowIfCancellationRequested();
                    var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                    throw new FfmpegException(
                        $"ffmpeg delivered {k} of {frames} frames of the playback window [{window.StartFrame}, {window.EndFrame}) (exit code {exitCode}).",
                        exitCode,
                        process.StderrTail(ProcessResult.ExceptionTailLines),
                        process.DisplayCommand,
                        ex);
                }

                // Playback frames carry request id 0; scrub frames use the viewer's ids.
                var frame = new VideoFrame(buffer, _size.Width, _size.Height, _size.Width * 4, window.TimelineFrameOf(k), 0, pooled: true);
                await _frames.Writer.WriteAsync(frame, token).ConfigureAwait(false);
                queued = true;
                Interlocked.Increment(ref _framesRead);
                _firstFrame.TrySetResult();
            }
            finally
            {
                if (!queued)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
    }
}
