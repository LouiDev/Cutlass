using Cutlass.Media.Audio;

namespace Cutlass.Tests.Media.Preview;

/// <summary>
/// A scriptable <see cref="IAudioOutput"/>: it records enqueued buffers, rejects an empty buffer the way OpenAL does,
/// and lets a test set availability and the played position.
/// </summary>
internal sealed class FakeAudioOutput : IAudioOutput
{
    /// <summary>The buffers enqueued so far, as frame counts.</summary>
    public List<int> Enqueued { get; } = [];

    /// <inheritdoc />
    public bool IsAvailable { get; set; } = true;

    /// <inheritdoc />
    public string DeviceName => "Fake";

    /// <inheritdoc />
    public int SampleRate => 48_000;

    /// <inheritdoc />
    public int Channels => 2;

    /// <inheritdoc />
    public int BufferCount => 6;

    /// <inheritdoc />
    public int BufferFrames => 1024;

    /// <inheritdoc />
    public int FreeBuffers { get; set; } = 6;

    /// <inheritdoc />
    public int QueuedBuffers => Enqueued.Count;

    /// <inheritdoc />
    public long PlayedSamples { get; set; }

    /// <inheritdoc />
    public bool IsStarted { get; private set; }

    /// <inheritdoc />
    public int Underruns => 0;

    /// <inheritdoc />
    public void Open(int sampleRate, int channels)
    {
    }

    /// <inheritdoc />
    public bool Enqueue(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0 || samples.Length % Channels != 0)
        {
            throw new ArgumentException("A buffer holds 1 to 1024 whole frames.", nameof(samples));
        }

        if (FreeBuffers == 0)
        {
            return false;
        }

        FreeBuffers--;
        Enqueued.Add(samples.Length / Channels);
        return true;
    }

    /// <inheritdoc />
    public void Start() => IsStarted = true;

    /// <inheritdoc />
    public void Pause() => IsStarted = false;

    /// <inheritdoc />
    public void Stop() => IsStarted = false;

    /// <inheritdoc />
    public void Flush() => PlayedSamples = 0;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
