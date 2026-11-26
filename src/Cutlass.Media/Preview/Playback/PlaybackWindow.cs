using Cutlass.Core.Time;

namespace Cutlass.Media.Preview.Playback;

/// <summary>
/// One playback render window (TECH_BRIEF 8.2): <c>[StartFrame, EndFrame)</c> rendered by one video process with
/// <c>FrameStep = Speed</c> and, at speed 1, one PerTrack audio process. Video output frame <c>k</c> is timeline frame
/// <c>StartFrame + k * Speed</c>, and a window outputs <c>ceil(n / Speed)</c> frames. Its audio is
/// <c>SamplesAt(EndFrame) - SamplesAt(StartFrame)</c> samples, so consecutive windows are gapless and frame exact.
/// </summary>
/// <param name="Index">The window number, 0 for the first.</param>
/// <param name="StartFrame">The first timeline frame.</param>
/// <param name="EndFrame">The end frame (exclusive).</param>
/// <param name="Speed">The frame step.</param>
/// <param name="StartSample">The absolute first sample, <c>SamplesAt(StartFrame)</c>.</param>
/// <param name="EndSample">The absolute end sample, <c>SamplesAt(EndFrame)</c>.</param>
public sealed record PlaybackWindow(int Index, long StartFrame, long EndFrame, int Speed, long StartSample, long EndSample)
{
    /// <summary>The timeline frames the window covers, <c>n = EndFrame - StartFrame</c>.</summary>
    public long FrameCount => EndFrame - StartFrame;

    /// <summary>The video frames the window outputs, <c>ceil(n / Speed)</c>.</summary>
    public long OutputFrames => (FrameCount + Speed - 1) / Speed;

    /// <summary>The audio samples per channel of the window.</summary>
    public long SampleCount => EndSample - StartSample;

    /// <summary>
    /// The timeline frames one window covers: <c>floor(seconds * R)</c> rounded down to whole output frames, times the
    /// speed, and at least one step. Whole steps keep every window aligned to <c>start + k * Speed</c>.
    /// </summary>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="speed">The speed.</param>
    /// <param name="windowSeconds">The wall-clock window length.</param>
    /// <returns>The frames per window.</returns>
    public static long Length(FrameRate rate, int speed, double windowSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speed);
        var outputFrames = (long)Math.Floor(windowSeconds * rate.Value.Num / rate.Value.Den);
        return Math.Max(1, outputFrames) * speed;
    }

    /// <summary>The window with the given index, or null when it would start at or after <paramref name="endFrame"/>.</summary>
    /// <param name="index">The window number.</param>
    /// <param name="startFrame">Where playback starts.</param>
    /// <param name="endFrame">Where playback ends (exclusive).</param>
    /// <param name="speed">The speed.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="windowSeconds">The wall-clock window length.</param>
    /// <returns>The window.</returns>
    public static PlaybackWindow? At(int index, long startFrame, long endFrame, int speed, FrameRate rate, double windowSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        var length = Length(rate, speed, windowSeconds);
        var start = startFrame + (index * length);
        if (start >= endFrame)
        {
            return null;
        }

        var end = Math.Min(start + length, endFrame);
        return new PlaybackWindow(index, start, end, speed, TimeMath.SamplesAt(start, rate), TimeMath.SamplesAt(end, rate));
    }

    /// <summary>Every window of a playback range, in order.</summary>
    /// <param name="startFrame">Where playback starts.</param>
    /// <param name="endFrame">Where playback ends (exclusive).</param>
    /// <param name="speed">The speed.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="windowSeconds">The wall-clock window length.</param>
    /// <returns>The windows.</returns>
    public static IReadOnlyList<PlaybackWindow> Plan(long startFrame, long endFrame, int speed, FrameRate rate, double windowSeconds)
    {
        var windows = new List<PlaybackWindow>();
        for (var i = 0; At(i, startFrame, endFrame, speed, rate, windowSeconds) is { } window; i++)
        {
            windows.Add(window);
        }

        return windows;
    }

    /// <summary>
    /// How many output frames before the end of a window the next window starts: the output frames shown in
    /// <paramref name="prefetchSeconds"/> of wall time (one output frame lasts <c>1/R</c> at every speed).
    /// </summary>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="prefetchSeconds">The lead time (TECH_BRIEF 8.2: 3 s).</param>
    /// <returns>The threshold in output frames.</returns>
    public static long PrefetchOutputFrames(FrameRate rate, double prefetchSeconds) =>
        (long)Math.Ceiling(prefetchSeconds * rate.Value.Num / rate.Value.Den);

    /// <summary>The timeline frame of output frame <paramref name="k"/>.</summary>
    /// <param name="k">The output frame index within the window.</param>
    /// <returns>The timeline frame.</returns>
    public long TimelineFrameOf(long k) => StartFrame + (k * Speed);
}
