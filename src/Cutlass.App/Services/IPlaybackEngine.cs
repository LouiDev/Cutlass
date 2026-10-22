using Cutlass.Media.Preview;

namespace Cutlass.App.Services;

/// <summary>
/// The part of <see cref="PlaybackSession"/> the viewer drives (TECH_BRIEF 8.2). It exists so the transport logic in
/// <see cref="ViewModels.PlaybackController"/> is unit tested without ffmpeg; <see cref="PlaybackEngine"/> forwards
/// every member to the real session.
/// </summary>
public interface IPlaybackEngine
{
    /// <summary>Raised on a worker thread when a run reached its end frame without a loop.</summary>
    event Action? ReachedEnd;

    /// <summary>Raised on a worker thread when a run failed.</summary>
    event Action<string>? Failed;

    /// <summary>
    /// Raised on a worker thread when the audio output became unavailable during a run (a disconnected device);
    /// <see cref="HasAudio"/> is then false and playback continues on a silent clock.
    /// </summary>
    event Action? AudioUnavailable;

    /// <summary>True while a run plays.</summary>
    bool IsPlaying { get; }

    /// <summary>The clock frame of the current run, or the parked frame.</summary>
    long Position { get; }

    /// <summary>Health of the current run.</summary>
    PlaybackStats Stats { get; }

    /// <summary>True when the audio output is a real device (false: silent Stopwatch clock).</summary>
    bool HasAudio { get; }

    /// <summary>Starts a run (stops the previous one first). Never blocks.</summary>
    /// <param name="options">What to play.</param>
    void Start(PlaybackOptions options);

    /// <summary>Stops without blocking and returns the frame to park the playhead on.</summary>
    /// <returns>The parked frame.</returns>
    long Stop();

    /// <summary>The newest queued frame at or before <paramref name="target"/> (UI thread).</summary>
    /// <param name="target">The clock frame.</param>
    /// <param name="frame">The frame; the caller returns it.</param>
    /// <param name="dropped">Frames skipped by this call.</param>
    /// <returns>True when a new frame is due.</returns>
    bool TryTakeFrame(long target, out VideoFrame? frame, out int dropped);

    /// <summary>Applies mute, solo and gain live.</summary>
    /// <param name="state">The mixer state.</param>
    void SetMixer(MixerState state);

    /// <summary>The peaks since the last call.</summary>
    /// <returns>The peaks.</returns>
    MixerPeaks ReadAndResetPeaks();
}
