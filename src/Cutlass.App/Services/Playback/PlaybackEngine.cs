using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Preview.Playback;

namespace Cutlass.App.Services.Playback;

/// <summary>Forwards <see cref="IPlaybackEngine"/> to a real <see cref="PlaybackSession"/>.</summary>
public sealed class PlaybackEngine : IPlaybackEngine
{
    private readonly PlaybackSession _session;

    /// <summary>Wraps a session.</summary>
    /// <param name="session">The session (owned by the caller).</param>
    public PlaybackEngine(PlaybackSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    /// <inheritdoc />
    public event Action? ReachedEnd
    {
        add => _session.ReachedEnd += value;
        remove => _session.ReachedEnd -= value;
    }

    /// <inheritdoc />
    public event Action<string>? Failed
    {
        add => _session.Failed += value;
        remove => _session.Failed -= value;
    }

    /// <inheritdoc />
    public event Action? AudioUnavailable
    {
        add => _session.AudioUnavailable += value;
        remove => _session.AudioUnavailable -= value;
    }

    /// <summary>The wrapped session.</summary>
    public PlaybackSession Session => _session;

    /// <inheritdoc />
    public bool IsPlaying => _session.IsPlaying;

    /// <inheritdoc />
    public long Position => _session.Position;

    /// <inheritdoc />
    public PlaybackStats Stats => _session.Stats;

    /// <inheritdoc />
    public bool HasAudio => _session.Audio.IsAvailable;

    /// <inheritdoc />
    public void Start(PlaybackOptions options) => _session.Start(options);

    /// <inheritdoc />
    public long Stop() => _session.Stop();

    /// <inheritdoc />
    public bool TryTakeFrame(long target, out VideoFrame? frame, out int dropped) => _session.TryTakeFrame(target, out frame, out dropped);

    /// <inheritdoc />
    public void SetMixer(MixerState state) => _session.SetMixer(state);

    /// <inheritdoc />
    public MixerPeaks ReadAndResetPeaks() => _session.ReadAndResetPeaks();
}
