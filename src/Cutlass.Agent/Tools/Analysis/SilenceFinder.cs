using Cutlass.Core.Time;

namespace Cutlass.Agent.Tools.Analysis;

/// <summary>
/// Finds silences in per-interval RMS levels (AGENT_INTEGRATION 4.4): runs of intervals below the threshold that last
/// at least the minimum duration, shrunk by the padding on each side that borders sound (a silence that reaches the
/// start or the end of the analysed range is not shrunk there, because no sound needs protecting), converted to whole
/// frames inside the silence. Pure.
/// </summary>
public static class SilenceFinder
{
    /// <summary>Finds the silent frame ranges.</summary>
    /// <param name="rmsDb">RMS per interval in dBFS, starting at <paramref name="startFrame"/>.</param>
    /// <param name="intervalMs">The interval length.</param>
    /// <param name="thresholdDb">Intervals below this are silent.</param>
    /// <param name="minDurationMs">The shortest silence (before padding).</param>
    /// <param name="padMs">Sound kept on each side.</param>
    /// <param name="startFrame">The first analysed frame.</param>
    /// <param name="endFrame">The end of the analysed range (exclusive).</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The ranges <c>[Start, End)</c> in frames, in order.</returns>
    public static IReadOnlyList<(long Start, long End)> Find(
        IReadOnlyList<double> rmsDb,
        int intervalMs,
        double thresholdDb,
        int minDurationMs,
        int padMs,
        long startFrame,
        long endFrame,
        FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(rmsDb);
        var result = new List<(long, long)>();
        var count = rmsDb.Count;
        var i = 0;
        while (i < count)
        {
            if (rmsDb[i] >= thresholdDb)
            {
                i++;
                continue;
            }

            var j = i;
            while (j < count && rmsDb[j] < thresholdDb)
            {
                j++;
            }

            var startMs = (long)i * intervalMs;
            var endMs = (long)j * intervalMs;
            if (endMs - startMs >= minDurationMs)
            {
                var s = i > 0 ? startMs + padMs : startMs;
                var e = j < count ? endMs - padMs : endMs;
                var startF = i > 0 ? startFrame + CeilFrames(s, rate) : startFrame;
                var endF = j < count ? startFrame + FloorFrames(e, rate) : endFrame;
                endF = Math.Min(endF, endFrame);
                if (endF > startF)
                {
                    result.Add((startF, endF));
                }
            }

            i = j;
        }

        return result;
    }

    private static long CeilFrames(long ms, FrameRate rate)
    {
        var num = (Int128)ms * rate.Value.Num;
        var den = (Int128)1000 * rate.Value.Den;
        return (long)((num + den - 1) / den);
    }

    private static long FloorFrames(long ms, FrameRate rate) =>
        (long)((Int128)ms * rate.Value.Num / ((Int128)1000 * rate.Value.Den));
}
