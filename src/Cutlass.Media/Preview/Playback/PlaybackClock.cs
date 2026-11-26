using System.Diagnostics;
using Cutlass.Core.Time;

namespace Cutlass.Media.Preview.Playback;

/// <summary>
/// The playback clock (TECH_BRIEF 8.2). At speed 1 audio is the master: the feeder publishes
/// <c>clockSamples = SamplesAt(StartFrame) + playedSamples</c> with a Stopwatch timestamp, readers extrapolate from the
/// last publish by at most 20 ms, and <c>ClockFrame = FrameAtSample(clockSamples)</c>. At speeds 2 and 4 a Stopwatch
/// drives <c>start + floor(elapsed * R * Speed)</c>. The clock never runs past <see cref="EndFrame"/> and never goes
/// backwards. Publishing happens on the feeder thread; reading is safe from any thread.
/// </summary>
public sealed class PlaybackClock
{
    /// <summary>How far a reader may extrapolate past the last audio publish.</summary>
    public static readonly TimeSpan MaxExtrapolation = TimeSpan.FromMilliseconds(20);

    private Published? _published;
    private long _stopwatchStart;
    private int _running;
    private long _lastFrame;

    /// <summary>Creates a stopped clock parked on <paramref name="startFrame"/>.</summary>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="startFrame">Where playback starts.</param>
    /// <param name="endFrame">Where playback ends (exclusive); the clock stops there.</param>
    /// <param name="speed">The speed (1, 2 or 4).</param>
    /// <param name="audioMaster">True when audio drives the clock (speed 1), false for the Stopwatch.</param>
    public PlaybackClock(FrameRate rate, long startFrame, long endFrame, int speed, bool audioMaster)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(speed);
        Rate = rate;
        StartFrame = startFrame;
        EndFrame = Math.Max(startFrame, endFrame);
        Speed = speed;
        AudioMaster = audioMaster;
        StartSample = TimeMath.SamplesAt(startFrame, rate);
        EndSample = TimeMath.SamplesAt(EndFrame, rate);
        _lastFrame = startFrame;
    }

    /// <summary>The sequence rate.</summary>
    public FrameRate Rate { get; }

    /// <summary>Where playback started.</summary>
    public long StartFrame { get; }

    /// <summary>Where playback ends (exclusive).</summary>
    public long EndFrame { get; }

    /// <summary>The speed.</summary>
    public int Speed { get; }

    /// <summary>True when audio drives the clock.</summary>
    public bool AudioMaster { get; }

    /// <summary><c>SamplesAt(StartFrame)</c>.</summary>
    public long StartSample { get; }

    /// <summary><c>SamplesAt(EndFrame)</c>.</summary>
    public long EndSample { get; }

    /// <summary>True once the clock started (the first audio publish, or <see cref="StartStopwatch"/>).</summary>
    public bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>The current timeline frame.</summary>
    public long Frame => FrameAt(Stopwatch.GetTimestamp());

    /// <summary>True once the clock reached <see cref="EndFrame"/>.</summary>
    public bool IsAtEnd => Frame >= EndFrame;

    /// <summary>
    /// The audio clock frame: <c>FrameAtSample(SamplesAt(start) + played + extrapolation)</c>, where the extrapolation
    /// is the time since the publish, at most 20 ms, in samples. Clamped to <paramref name="endFrame"/>.
    /// </summary>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="startFrame">Where playback started.</param>
    /// <param name="endFrame">Where playback ends.</param>
    /// <param name="playedSamples">The output's played samples at the publish.</param>
    /// <param name="ticksSincePublish">Stopwatch ticks since the publish.</param>
    /// <param name="frequency">Stopwatch ticks per second.</param>
    /// <returns>The frame.</returns>
    public static long AudioFrame(FrameRate rate, long startFrame, long endFrame, long playedSamples, long ticksSincePublish, long frequency)
    {
        var sample = AudioSample(rate, startFrame, endFrame, playedSamples, ticksSincePublish, frequency);
        return Math.Min(endFrame, TimeMath.FrameAtSample(sample, rate));
    }

    /// <summary>The absolute audio clock sample; see <see cref="AudioFrame"/>.</summary>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="startFrame">Where playback started.</param>
    /// <param name="endFrame">Where playback ends.</param>
    /// <param name="playedSamples">The output's played samples at the publish.</param>
    /// <param name="ticksSincePublish">Stopwatch ticks since the publish.</param>
    /// <param name="frequency">Stopwatch ticks per second.</param>
    /// <returns>The absolute sample, clamped to <c>SamplesAt(endFrame)</c>.</returns>
    public static long AudioSample(FrameRate rate, long startFrame, long endFrame, long playedSamples, long ticksSincePublish, long frequency)
    {
        var maxTicks = (long)(MaxExtrapolation.TotalSeconds * frequency);
        var extraTicks = Math.Clamp(ticksSincePublish, 0, maxTicks);
        var extraSamples = (long)((Int128)extraTicks * TimeMath.SampleRate / frequency);
        var sample = TimeMath.SamplesAt(startFrame, rate) + Math.Max(0, playedSamples) + extraSamples;
        return Math.Min(TimeMath.SamplesAt(endFrame, rate), sample);
    }

    /// <summary>The Stopwatch clock frame, <c>start + floor(elapsed * R * Speed)</c>, clamped to <paramref name="endFrame"/>.</summary>
    /// <param name="rate">The sequence rate.</param>
    /// <param name="startFrame">Where playback started.</param>
    /// <param name="endFrame">Where playback ends.</param>
    /// <param name="speed">The speed.</param>
    /// <param name="elapsedTicks">Stopwatch ticks since the clock started.</param>
    /// <param name="frequency">Stopwatch ticks per second.</param>
    /// <returns>The frame.</returns>
    public static long StopwatchFrame(FrameRate rate, long startFrame, long endFrame, int speed, long elapsedTicks, long frequency)
    {
        var steps = (Int128)Math.Max(0, elapsedTicks) * rate.Value.Num * speed / ((Int128)rate.Value.Den * frequency);
        return (long)Int128.Min(endFrame, startFrame + steps);
    }

    /// <summary>Publishes the audio position (feeder thread). The first publish starts the clock.</summary>
    /// <param name="playedSamples">The output's <c>PlayedSamples</c>.</param>
    /// <param name="timestamp">The <see cref="Stopwatch.GetTimestamp"/> of the reading.</param>
    public void PublishSamples(long playedSamples, long timestamp)
    {
        Volatile.Write(ref _published, new Published(playedSamples, timestamp));
        Volatile.Write(ref _running, 1);
    }

    /// <summary>Starts the Stopwatch clock (speeds 2 and 4).</summary>
    /// <param name="timestamp">The <see cref="Stopwatch.GetTimestamp"/> of the start.</param>
    public void StartStopwatch(long timestamp)
    {
        Volatile.Write(ref _stopwatchStart, timestamp);
        Volatile.Write(ref _running, 1);
    }

    /// <summary>The absolute audio sample at a moment (the start sample while the clock is stopped).</summary>
    /// <param name="timestamp">The <see cref="Stopwatch.GetTimestamp"/> of the moment.</param>
    /// <returns>The sample.</returns>
    public long SampleAt(long timestamp)
    {
        if (!IsRunning)
        {
            return StartSample;
        }

        if (!AudioMaster)
        {
            return TimeMath.SamplesAt(FrameAt(timestamp), Rate);
        }

        var published = Volatile.Read(ref _published);
        return published is null
            ? StartSample
            : AudioSample(Rate, StartFrame, EndFrame, published.Samples, timestamp - published.Timestamp, Stopwatch.Frequency);
    }

    /// <summary>The timeline frame at a moment. Monotonic: never less than a frame returned before.</summary>
    /// <param name="timestamp">The <see cref="Stopwatch.GetTimestamp"/> of the moment.</param>
    /// <returns>The frame.</returns>
    public long FrameAt(long timestamp)
    {
        long frame;
        if (!IsRunning)
        {
            frame = StartFrame;
        }
        else if (AudioMaster)
        {
            var published = Volatile.Read(ref _published);
            frame = published is null
                ? StartFrame
                : AudioFrame(Rate, StartFrame, EndFrame, published.Samples, timestamp - published.Timestamp, Stopwatch.Frequency);
        }
        else
        {
            frame = StopwatchFrame(Rate, StartFrame, EndFrame, Speed, timestamp - Volatile.Read(ref _stopwatchStart), Stopwatch.Frequency);
        }

        // Extrapolation can overshoot a stalled publish by up to 20 ms; the clock then holds instead of stepping back.
        var last = Interlocked.Read(ref _lastFrame);
        while (frame > last)
        {
            var seen = Interlocked.CompareExchange(ref _lastFrame, frame, last);
            if (seen == last)
            {
                return frame;
            }

            last = seen;
        }

        return last;
    }

    private sealed record Published(long Samples, long Timestamp);
}
