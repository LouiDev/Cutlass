using System.Diagnostics;

namespace Cutlass.Media.Audio;

/// <summary>
/// The silent fallback when no OpenAL device opens (TECH_BRIEF 5.6): nothing is heard, and <see cref="PlayedSamples"/>
/// is a monotonic Stopwatch clock (<c>floor(elapsed * SampleRate)</c> while started). It paces the feeder like a
/// device would: a buffer counts as free once the clock has passed it.
/// </summary>
public sealed class NullAudioOutput : IAudioOutput
{
    private readonly object _gate = new();
    private long _accumulatedTicks;
    private long _startedAt;
    private bool _started;
    private long _enqueuedFrames;
    private long _lastPlayed;

    /// <summary>Creates the output.</summary>
    /// <param name="bufferCount">How many buffers the virtual queue holds.</param>
    /// <param name="bufferFrames">The frames per buffer.</param>
    public NullAudioOutput(int bufferCount = OpenAlAudioOutput.DefaultBufferCount, int bufferFrames = OpenAlAudioOutput.DefaultBufferFrames)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferFrames);
        BufferCount = bufferCount;
        BufferFrames = bufferFrames;
    }

    /// <inheritdoc />
    public bool IsAvailable => false;

    /// <inheritdoc />
    public string DeviceName => "No audio device (silent clock)";

    /// <inheritdoc />
    public int SampleRate { get; private set; }

    /// <inheritdoc />
    public int Channels { get; private set; }

    /// <inheritdoc />
    public int BufferCount { get; }

    /// <inheritdoc />
    public int BufferFrames { get; }

    /// <inheritdoc />
    public int FreeBuffers
    {
        get
        {
            lock (_gate)
            {
                return BufferCount - QueuedLocked();
            }
        }
    }

    /// <inheritdoc />
    public int QueuedBuffers
    {
        get
        {
            lock (_gate)
            {
                return QueuedLocked();
            }
        }
    }

    /// <inheritdoc />
    public long PlayedSamples
    {
        get
        {
            lock (_gate)
            {
                return PlayedLocked();
            }
        }
    }

    /// <inheritdoc />
    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>The silent clock never runs dry.</remarks>
    public int Underruns => 0;

    /// <inheritdoc />
    public void Open(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 2);
        lock (_gate)
        {
            SampleRate = sampleRate;
            Channels = channels;
            FlushLocked();
        }
    }

    /// <inheritdoc />
    public bool Enqueue(ReadOnlySpan<short> samples)
    {
        lock (_gate)
        {
            if (Channels == 0)
            {
                throw new InvalidOperationException("Open the output before enqueueing samples.");
            }

            if (samples.Length % Channels != 0 || samples.Length / Channels > BufferFrames)
            {
                throw new ArgumentException($"A buffer holds whole frames, at most {BufferFrames}.", nameof(samples));
            }

            if (QueuedLocked() >= BufferCount)
            {
                return false;
            }

            // A clock that ran ahead of the data (the feeder fell behind) does not make later data count as played.
            _enqueuedFrames = Math.Max(_enqueuedFrames, PlayedLocked()) + (samples.Length / Channels);
            return true;
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (!_started)
            {
                _started = true;
                _startedAt = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        lock (_gate)
        {
            if (_started)
            {
                _accumulatedTicks += Stopwatch.GetTimestamp() - _startedAt;
                _started = false;
            }
        }
    }

    /// <inheritdoc />
    public void Stop() => Pause();

    /// <inheritdoc />
    public void Flush()
    {
        lock (_gate)
        {
            FlushLocked();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Flush();
    }

    private void FlushLocked()
    {
        _started = false;
        _accumulatedTicks = 0;
        _enqueuedFrames = 0;
        _lastPlayed = 0;
    }

    private long PlayedLocked()
    {
        if (SampleRate == 0)
        {
            return 0;
        }

        var ticks = _accumulatedTicks + (_started ? Stopwatch.GetTimestamp() - _startedAt : 0);
        var played = (long)((Int128)ticks * SampleRate / Stopwatch.Frequency);
        _lastPlayed = Math.Max(_lastPlayed, played);
        return _lastPlayed;
    }

    private int QueuedLocked()
    {
        var pending = _enqueuedFrames - PlayedLocked();
        return pending <= 0 ? 0 : (int)Math.Min(BufferCount, (pending + BufferFrames - 1) / BufferFrames);
    }
}
