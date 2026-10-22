using Cutlass.Media.Audio;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>
/// A headless audio output for playback tests: a <see cref="NullAudioOutput"/> (the Stopwatch clock the app falls back
/// to without a device) that also records every enqueued stereo sample, so tests can check what would have been heard
/// and where.
/// </summary>
internal sealed class RecordingAudioOutput : IAudioOutput
{
    private readonly NullAudioOutput _inner = new();
    private readonly object _gate = new();
    private readonly List<short> _samples = new(48000 * 2 * 30);

    /// <summary>Every enqueued sample since the last open, interleaved stereo (a flush keeps the recording).</summary>
    public short[] Recorded
    {
        get
        {
            lock (_gate)
            {
                return [.. _samples];
            }
        }
    }

    /// <summary>The stereo frames enqueued since the last open.</summary>
    public long EnqueuedFrames
    {
        get
        {
            lock (_gate)
            {
                return _samples.Count / 2;
            }
        }
    }

    /// <summary>How often <see cref="Open"/> was called.</summary>
    public int Opens { get; private set; }

    /// <inheritdoc />
    public bool IsAvailable => _inner.IsAvailable;

    /// <inheritdoc />
    public string DeviceName => "Recording test output";

    /// <inheritdoc />
    public int SampleRate => _inner.SampleRate;

    /// <inheritdoc />
    public int Channels => _inner.Channels;

    /// <inheritdoc />
    public int BufferCount => _inner.BufferCount;

    /// <inheritdoc />
    public int BufferFrames => _inner.BufferFrames;

    /// <inheritdoc />
    public int FreeBuffers => _inner.FreeBuffers;

    /// <inheritdoc />
    public int QueuedBuffers => _inner.QueuedBuffers;

    /// <inheritdoc />
    public long PlayedSamples => _inner.PlayedSamples;

    /// <inheritdoc />
    public bool IsStarted => _inner.IsStarted;

    /// <inheritdoc />
    public int Underruns => _inner.Underruns;

    /// <inheritdoc />
    public void Open(int sampleRate, int channels)
    {
        lock (_gate)
        {
            _inner.Open(sampleRate, channels);
            _samples.Clear();
            Opens++;
        }
    }

    /// <inheritdoc />
    public bool Enqueue(ReadOnlySpan<short> samples)
    {
        lock (_gate)
        {
            if (!_inner.Enqueue(samples))
            {
                return false;
            }

            foreach (var s in samples)
            {
                _samples.Add(s);
            }

            return true;
        }
    }

    /// <inheritdoc />
    public void Start() => _inner.Start();

    /// <inheritdoc />
    public void Pause() => _inner.Pause();

    /// <inheritdoc />
    public void Stop() => _inner.Stop();

    /// <inheritdoc />
    public void Flush()
    {
        // The recording is kept for the assertions; only the clock and the queue reset.
        _inner.Flush();
    }

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
