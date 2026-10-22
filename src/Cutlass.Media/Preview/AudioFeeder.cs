using System.Diagnostics;
using System.Threading.Channels;
using Cutlass.Media.Audio;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Preview;

/// <summary>
/// Feeds playback audio and drives the clock (TECH_BRIEF 8.2). A pump task reads the PerTrack s16le output of every
/// window, in order and gaplessly, into blocks of one output buffer (1024 frames). A dedicated thread then, every
/// 5 ms: reclaims played buffers, mixes blocks to stereo with the <see cref="AudioMixer"/> and queues them, keeping
/// every buffer (128 ms) queued; starts the output once the first video frame is ready and the queue is full; and
/// publishes <c>PlayedSamples</c> to the <see cref="PlaybackClock"/>. At speeds 2 and 4 there is no audio and the
/// thread starts the Stopwatch clock instead. It makes every <see cref="IAudioOutput"/> call while playing. When the
/// output becomes unavailable (a disconnected device) the clock continues on a Stopwatch through
/// <see cref="AudioClockSource"/>, the blocks are dropped at the rate they would have played, and the loss is reported.
/// </summary>
internal sealed class AudioFeeder
{
    /// <summary>The feeder period.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(5);

    /// <summary>How many mixed-down blocks the PCM queue holds (about 1 s at 1024 frames).</summary>
    public const int BlockQueueCapacity = 48;

    private readonly PlaybackPipeline _pipeline;
    private readonly IAudioOutput _audio;
    private readonly AudioMixer _mixer;
    private readonly PlaybackClock _clock;
    private readonly Task _firstVideoFrame;
    private readonly Func<MixerState> _mixerState;
    private readonly Action<long> _positionChanged;
    private readonly Action _reachedEnd;
    private readonly Action<Exception> _failed;
    private readonly Action _audioLost;
    private readonly Channel<PcmBlock> _blocks = Channel.CreateBounded<PcmBlock>(new BoundedChannelOptions(BlockQueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = true,
    });

    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task _pump = Task.CompletedTask;
    private long _enqueuedFrames;
    private int _outputStarted;

    /// <summary>Creates the feeder.</summary>
    /// <param name="pipeline">Starts the window processes.</param>
    /// <param name="audio">The output (opened by the caller).</param>
    /// <param name="mixer">Mixes PerTrack audio to stereo.</param>
    /// <param name="clock">The clock to drive.</param>
    /// <param name="firstVideoFrame">Completes when the first video frame is queued.</param>
    /// <param name="mixerState">The current mixer state, baked into a Mixed fallback graph.</param>
    /// <param name="positionChanged">Called on the feeder thread when the clock frame changes.</param>
    /// <param name="reachedEnd">Called on the feeder thread when the clock reaches the end.</param>
    /// <param name="failed">Called when the audio pump fails.</param>
    /// <param name="audioLost">Called on the feeder thread when the output became unavailable during the run.</param>
    public AudioFeeder(
        PlaybackPipeline pipeline,
        IAudioOutput audio,
        AudioMixer mixer,
        PlaybackClock clock,
        Task firstVideoFrame,
        Func<MixerState> mixerState,
        Action<long> positionChanged,
        Action reachedEnd,
        Action<Exception> failed,
        Action audioLost)
    {
        _audioLost = audioLost;
        _pipeline = pipeline;
        _audio = audio;
        _mixer = mixer;
        _clock = clock;
        _firstVideoFrame = firstVideoFrame;
        _mixerState = mixerState;
        _positionChanged = positionChanged;
        _reachedEnd = reachedEnd;
        _failed = failed;
    }

    /// <summary>Completes when the feeder thread ended.</summary>
    public Task Completion => _completion.Task;

    /// <summary>The audio pump (completed at speeds above 1).</summary>
    public Task Pump => _pump;

    /// <summary>The mixed frames queued on the output so far.</summary>
    public long EnqueuedFrames => Interlocked.Read(ref _enqueuedFrames);

    /// <summary>True once the output was started.</summary>
    public bool OutputStarted => Volatile.Read(ref _outputStarted) != 0;

    /// <summary>Starts the pump (speed 1) and the feeder thread.</summary>
    /// <param name="token">Stops both.</param>
    public void Start(CancellationToken token)
    {
        if (_pipeline.HasAudio)
        {
            _pump = Task.Run(() => PumpAsync(token), CancellationToken.None);
        }
        else
        {
            _blocks.Writer.TryComplete();
        }

        var thread = new Thread(() => Loop(token))
        {
            IsBackground = true,
            Name = "Cutlass audio feeder",
            Priority = ThreadPriority.AboveNormal,
        };
        thread.Start();
    }

    /// <summary>Returns every queued block to the pool (after the feeder ended).</summary>
    public void DrainBlocks()
    {
        while (_blocks.Reader.TryRead(out var block))
        {
            block.Return();
        }
    }

    private void Loop(CancellationToken token)
    {
        PcmBlock? pending = null;
        try
        {
            var stereo = new short[_audio.BufferFrames * 2];
            var audioMode = _pipeline.HasAudio;
            var source = new AudioClockSource(_audio);
            var queueFrames = (long)_audio.BufferCount * _audio.BufferFrames;
            var started = false;
            var lastFrame = long.MinValue;
            while (!token.IsCancellationRequested)
            {
                if (_firstVideoFrame.IsFaulted || _firstVideoFrame.IsCanceled)
                {
                    // The video reader failed; the session reports it.
                    return;
                }

                if (audioMode)
                {
                    var now = Stopwatch.GetTimestamp();
                    source.Poll(now);
                    if (source.FellBack)
                    {
                        // Drop what the lost output would have played, so the pump and the windows keep moving.
                        var budget = source.PlayedSamples(now, Stopwatch.Frequency) + queueFrames - EnqueuedFrames;
                        Interlocked.Add(ref _enqueuedFrames, DiscardBlocks(_blocks.Reader, ref pending, budget));
                    }
                    else
                    {
                        Interlocked.Add(ref _enqueuedFrames, FeedOutput(_audio, _mixer, _blocks.Reader, ref pending, stereo));
                    }

                    if (_pump.IsFaulted)
                    {
                        _failed(_pump.Exception!.GetBaseException());
                        return;
                    }

                    var inputDone = pending is null && _blocks.Reader.Completion.IsCompleted;
                    if (!started && _firstVideoFrame.IsCompletedSuccessfully
                        && (source.FellBack || _audio.FreeBuffers == 0 || inputDone))
                    {
                        // SourcePlay waits for the first video frame and a full queue (TECH_BRIEF 8.2).
                        if (!source.FellBack)
                        {
                            _audio.Start();
                        }

                        source.Start(Stopwatch.GetTimestamp());
                        started = true;
                        Volatile.Write(ref _outputStarted, 1);
                    }

                    if (started)
                    {
                        var timestamp = Stopwatch.GetTimestamp();
                        _clock.PublishSamples(source.PlayedSamples(timestamp, Stopwatch.Frequency), timestamp);
                    }

                    if (source.TakeLost())
                    {
                        _audioLost();
                    }
                }
                else if (!started && _firstVideoFrame.IsCompletedSuccessfully)
                {
                    _clock.StartStopwatch(Stopwatch.GetTimestamp());
                    started = true;
                }

                var frame = _clock.Frame;
                if (frame != lastFrame)
                {
                    lastFrame = frame;
                    _positionChanged(frame);
                }

                if (started && frame >= _clock.EndFrame)
                {
                    _reachedEnd();
                    return;
                }

                Thread.Sleep(Tick);
            }
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            _failed(ex);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Stopped while an output call failed; nothing to report.
        }
        finally
        {
            pending?.Return();
            _completion.TrySetResult();
        }
    }

    /// <summary>
    /// Mixes queued blocks to stereo and enqueues them while the output has free buffers. A block without frames is
    /// returned to the pool and skipped: the output rejects an empty buffer.
    /// </summary>
    /// <param name="audio">The output.</param>
    /// <param name="mixer">The mixer; its <see cref="AudioMixer.Premixed"/> follows each block.</param>
    /// <param name="blocks">The block queue.</param>
    /// <param name="pending">A block read earlier that did not fit yet; updated.</param>
    /// <param name="stereo">Scratch space for one mixed buffer.</param>
    /// <returns>The frames enqueued.</returns>
    internal static long FeedOutput(
        IAudioOutput audio,
        AudioMixer mixer,
        ChannelReader<PcmBlock> blocks,
        ref PcmBlock? pending,
        short[] stereo)
    {
        long enqueued = 0;
        while (audio.FreeBuffers > 0)
        {
            if (pending is null && !blocks.TryRead(out pending))
            {
                break;
            }

            if (pending.Frames == 0)
            {
                pending.Return();
                pending = null;
                continue;
            }

            if (pending.Premixed != mixer.Premixed)
            {
                mixer.Premixed = pending.Premixed;
            }

            mixer.Mix(pending.Samples, stereo, pending.Frames);
            if (!audio.Enqueue(stereo.AsSpan(0, pending.Frames * 2)))
            {
                break;
            }

            enqueued += pending.Frames;
            pending.Return();
            pending = null;
        }

        return enqueued;
    }

    /// <summary>Drops queued blocks (returning them to the pool) until <paramref name="budgetFrames"/> frames went.</summary>
    /// <param name="blocks">The block queue.</param>
    /// <param name="pending">A block read earlier; updated.</param>
    /// <param name="budgetFrames">How many frames may go now.</param>
    /// <returns>The frames dropped.</returns>
    internal static long DiscardBlocks(ChannelReader<PcmBlock> blocks, ref PcmBlock? pending, long budgetFrames)
    {
        long dropped = 0;
        while (dropped < budgetFrames)
        {
            if (pending is null && !blocks.TryRead(out pending))
            {
                break;
            }

            dropped += pending.Frames;
            pending.Return();
            pending = null;
        }

        return dropped;
    }

    private async Task PumpAsync(CancellationToken token)
    {
        PcmBlock? block = null;
        try
        {
            var blockFrames = _audio.BufferFrames;
            for (var index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                if (_pipeline.Audio(index) is not { } current)
                {
                    break;
                }

                var (window, process, channels, premixed) = current;
                var remaining = window.SampleCount;
                long delivered = 0;
                while (remaining > 0)
                {
                    if (remaining <= _pipeline.PrefetchSamples)
                    {
                        _pipeline.EnsureStarted(index + 1);
                    }

                    if (block is not null && (block.Channels != channels || block.Premixed != premixed))
                    {
                        await PushAsync(block, token).ConfigureAwait(false);
                        block = null;
                    }

                    block ??= new PcmBlock(blockFrames, channels, premixed);
                    var n = (int)Math.Min(block.CapacityFrames - block.Frames, remaining);
                    try
                    {
                        await process.Stdout.ReadExactlyAsync(block.Free(n), token).ConfigureAwait(false);
                    }
                    catch (EndOfStreamException ex)
                    {
                        token.ThrowIfCancellationRequested();
                        var exitCode = await process.Exited.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                        if (delivered == 0 && !premixed && exitCode != 0 && _pipeline.RestartAudioMixed(index, _mixerState()) is { } fallback)
                        {
                            // PerTrack failed at runtime: continue with a Mixed graph (TECH_BRIEF 7.6).
                            process = fallback.Process;
                            channels = fallback.Channels;
                            premixed = true;
                            continue;
                        }

                        throw new FfmpegException(
                            $"ffmpeg delivered {delivered} of {window.SampleCount} samples of the playback window [{window.StartFrame}, {window.EndFrame}) (exit code {exitCode}).",
                            exitCode,
                            process.StderrTail(ProcessResult.ExceptionTailLines),
                            process.DisplayCommand,
                            ex);
                    }

                    block.Frames += n;
                    remaining -= n;
                    delivered += n;
                    if (block.IsFull)
                    {
                        await PushAsync(block, token).ConfigureAwait(false);
                        block = null;
                    }
                }

                await _pipeline.ReleaseAsync(process).ConfigureAwait(false);
            }

            if (block is not null)
            {
                await PushAsync(block, token).ConfigureAwait(false);
                block = null;
            }

            _blocks.Writer.TryComplete();
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            block?.Return();
            _blocks.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            block?.Return();
            _blocks.Writer.TryComplete(ex);
            throw;
        }
    }

    private async Task PushAsync(PcmBlock block, CancellationToken token)
    {
        if (block.Frames == 0)
        {
            // The PerTrack to Mixed fallback can leave an empty block behind; it carries nothing to play.
            block.Return();
            return;
        }

        try
        {
            await _blocks.Writer.WriteAsync(block, token).ConfigureAwait(false);
        }
        catch
        {
            block.Return();
            throw;
        }
    }
}
