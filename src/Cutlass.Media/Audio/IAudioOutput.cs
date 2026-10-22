namespace Cutlass.Media.Audio;

/// <summary>
/// A streaming PCM output for preview playback (TECH_BRIEF 5.6 and 8.2): a queue of <see cref="BufferCount"/> buffers
/// of interleaved signed 16-bit samples. The playback clock is derived from <see cref="PlayedSamples"/>.
/// While playing, the audio feeder is the only caller; implementations still serialise calls internally, so the
/// owner may stop and flush the output from another thread once the feeder has ended.
/// </summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>True for a real device, false for <see cref="NullAudioOutput"/> (the status bar shows "Audio unavailable").</summary>
    bool IsAvailable { get; }

    /// <summary>The device name, or a short description of the fallback.</summary>
    string DeviceName { get; }

    /// <summary>The sample rate set by <see cref="Open"/> (0 before).</summary>
    int SampleRate { get; }

    /// <summary>The channel count set by <see cref="Open"/> (0 before).</summary>
    int Channels { get; }

    /// <summary>How many buffers the queue holds (6 for OpenAL, 128 ms at 1024 frames each).</summary>
    int BufferCount { get; }

    /// <summary>The nominal frames per buffer (1024). <see cref="Enqueue"/> accepts up to this many frames.</summary>
    int BufferFrames { get; }

    /// <summary>How many buffers can be enqueued now. Reading it reclaims the buffers that finished playing.</summary>
    int FreeBuffers { get; }

    /// <summary>How many buffers are queued and not yet played.</summary>
    int QueuedBuffers { get; }

    /// <summary>
    /// The frames (samples per channel) played since <see cref="Open"/> or the last <see cref="Flush"/>: the frames of
    /// every finished buffer plus the play offset into the current one (<c>AL_SAMPLE_OFFSET</c>). Never decreases
    /// until the next flush.
    /// </summary>
    long PlayedSamples { get; }

    /// <summary>True between <see cref="Start"/> and <see cref="Pause"/>, <see cref="Stop"/> or <see cref="Flush"/>.</summary>
    bool IsStarted { get; }

    /// <summary>How many times the output ran dry while started and had to be restarted (since <see cref="Open"/>).</summary>
    int Underruns { get; }

    /// <summary>Prepares the output for a format and flushes it. Calling it again with the same format only flushes.</summary>
    /// <param name="sampleRate">The sample rate (48000).</param>
    /// <param name="channels">1 or 2.</param>
    void Open(int sampleRate, int channels);

    /// <summary>
    /// Queues one buffer of interleaved samples (a whole number of frames, at most <see cref="BufferFrames"/>).
    /// </summary>
    /// <param name="samples">The samples.</param>
    /// <returns>False when no buffer is free (nothing was queued).</returns>
    bool Enqueue(ReadOnlySpan<short> samples);

    /// <summary>Starts or resumes playback of the queue.</summary>
    void Start();

    /// <summary>Pauses playback, keeping the queue and the position.</summary>
    void Pause();

    /// <summary>Stops playback. Queued buffers count as played afterwards.</summary>
    void Stop();

    /// <summary>Stops playback, drops the queue and resets <see cref="PlayedSamples"/> to 0.</summary>
    void Flush();
}
