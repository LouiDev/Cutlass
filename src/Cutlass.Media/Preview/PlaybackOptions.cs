using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Media.Preview;

/// <summary>
/// What a <see cref="PlaybackSession"/> plays (TECH_BRIEF 8.2).
/// </summary>
/// <param name="Snapshot">The immutable project. Timeline edits dispose nothing here: the viewer restarts playback
/// with a new snapshot after 150 ms of quiet.</param>
/// <param name="StartFrame">The first timeline frame to play.</param>
/// <param name="EndFrame">The end frame (exclusive): the sequence end or the Out point.</param>
/// <param name="Speed">The forward speed: 1, 2 or 4. Audio plays only at 1.</param>
/// <param name="Size">The preview size (even).</param>
/// <param name="UseProxies">True to read video from proxies where one exists.</param>
/// <param name="Mixer">The live mixer state (mute, solo, gain).</param>
public sealed record PlaybackOptions(
    Project Snapshot,
    long StartFrame,
    long EndFrame,
    int Speed,
    PreviewSize Size,
    bool UseProxies,
    MixerState Mixer)
{
    /// <summary>The speeds playback supports (TECH_BRIEF 8.2). 0.5x is not offered: it would need an atempo graph.</summary>
    public static IReadOnlyList<int> SupportedSpeeds { get; } = [1, 2, 4];

    /// <summary>True to restart at <see cref="LoopStartFrame"/> when <see cref="EndFrame"/> is reached.</summary>
    public bool Loop { get; init; }

    /// <summary>Where a loop restarts (the In point); null restarts at <see cref="StartFrame"/>.</summary>
    public long? LoopStartFrame { get; init; }

    /// <summary>The wall-clock length of one render window (TECH_BRIEF 8.2: 10 s, covering 10 s * Speed of frames).</summary>
    public double WindowSeconds { get; init; } = 10.0;

    /// <summary>How long before the end of a window the next window's processes start (TECH_BRIEF 8.2: 3 s).</summary>
    public double PrefetchSeconds { get; init; } = 3.0;

    /// <summary>
    /// Options that play the whole sequence from <paramref name="startFrame"/> to its end, with the mixer state the
    /// model holds.
    /// </summary>
    /// <param name="snapshot">The project.</param>
    /// <param name="startFrame">The first frame.</param>
    /// <param name="speed">1, 2 or 4.</param>
    /// <param name="size">The preview size.</param>
    /// <param name="useProxies">True to use proxies.</param>
    /// <returns>The options.</returns>
    public static PlaybackOptions ForSequence(Project snapshot, long startFrame, int speed, PreviewSize size, bool useProxies)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PlaybackOptions(
            snapshot,
            startFrame,
            TimelineQuery.Duration(snapshot),
            speed,
            size,
            useProxies,
            MixerState.FromSequence(snapshot.Sequence));
    }

    /// <summary>Throws when the options cannot be played.</summary>
    /// <exception cref="ArgumentException">When a value is out of range.</exception>
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Snapshot);
        ArgumentNullException.ThrowIfNull(Size);
        ArgumentNullException.ThrowIfNull(Mixer);
        if (!SupportedSpeeds.Contains(Speed))
        {
            throw new ArgumentException($"Speed {Speed} is not supported; use 1, 2 or 4.", nameof(Speed));
        }

        if (!Size.IsValid)
        {
            throw new ArgumentException($"The preview size {Size.Width}x{Size.Height} must be positive and even.", nameof(Size));
        }

        if (StartFrame < 0 || EndFrame <= StartFrame)
        {
            throw new ArgumentException($"Cannot play [{StartFrame}, {EndFrame}).", nameof(EndFrame));
        }

        if (WindowSeconds <= 0 || PrefetchSeconds < 0)
        {
            throw new ArgumentException("The window length must be positive and the prefetch time not negative.", nameof(WindowSeconds));
        }
    }
}
