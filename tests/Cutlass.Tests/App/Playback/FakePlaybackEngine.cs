using Cutlass.App.Services.Playback;
using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Preview.Playback;

namespace Cutlass.Tests.App.Playback;

/// <summary>A scripted <see cref="IPlaybackEngine"/>: records starts, stops and mixer changes.</summary>
internal sealed class FakePlaybackEngine : IPlaybackEngine
{
    public event Action? ReachedEnd;

    public event Action? AudioUnavailable;

    public event Action<string>? Failed;

    /// <summary>Raises <see cref="AudioUnavailable"/> the way a disconnected device would.</summary>
    public void RaiseAudioUnavailable() => AudioUnavailable?.Invoke();

    public List<PlaybackOptions> Starts { get; } = [];

    public List<MixerState> Mixers { get; } = [];

    public int Stops { get; private set; }

    public bool IsPlaying { get; set; }

    public long Position { get; set; }

    public long ParkFrame { get; set; }

    public PlaybackStats Stats { get; set; } = PlaybackStats.Empty;

    /// <summary>False to act like an engine whose audio device is unavailable (a silent clock).</summary>
    public bool HasAudio { get; set; } = true;

    public Queue<VideoFrame> Frames { get; } = new();

    public void Start(PlaybackOptions options)
    {
        options.Validate();
        Starts.Add(options);
        IsPlaying = true;
        Position = options.StartFrame;
    }

    public long Stop()
    {
        Stops++;
        IsPlaying = false;
        return ParkFrame;
    }

    public bool TryTakeFrame(long target, out VideoFrame? frame, out int dropped)
    {
        dropped = 0;
        return Frames.TryDequeue(out frame);
    }

    public void SetMixer(MixerState state) => Mixers.Add(state);

    public MixerPeaks ReadAndResetPeaks() => new([], 0.5f, 0.25f);

    public void RaiseReachedEnd() => ReachedEnd?.Invoke();

    public void RaiseFailed(string message) => Failed?.Invoke(message);
}
