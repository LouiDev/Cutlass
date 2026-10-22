using System.Text;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Compiler.Graph;

/// <summary>
/// Builds the volume keyframe envelope of an audio segment (TECH_BRIEF 7.7):
/// <c>volume=volume='pow(10,(&lt;inner&gt;)/20)':eval=frame</c>, where <c>inner</c> interpolates linearly in dB
/// between keys and holds the first and last value outside them.
/// </summary>
public static class VolumeEnvelope
{
    /// <summary>Builds the envelope filter.</summary>
    /// <param name="keys">The clip's keys (frames relative to the clip start). Must not be empty.</param>
    /// <param name="offsetFrames">Frames from the extent start to the clip start (the head extension), so key times
    /// become seconds from the extent start.</param>
    /// <param name="gainDb">The clip gain, folded into every key.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The filter text.</returns>
    /// <exception cref="ArgumentException">When <paramref name="keys"/> is empty.</exception>
    public static string Filter(IReadOnlyList<VolumeKey> keys, long offsetFrames, double gainDb, FrameRate rate)
    {
        var inner = Expression(keys, offsetFrames, gainDb, rate);
        return "volume=volume=" + FilterEscaper.QuoteExpression("pow(10,(" + inner + ")/20)") + ":eval=frame";
    }

    /// <summary>
    /// Builds the inner dB expression of <c>t</c> (seconds from the extent start). Keys are sorted by frame (stable);
    /// a later key at the same frame replaces the jump, since a zero-length ramp is never evaluated.
    /// </summary>
    /// <param name="keys">The keys. Must not be empty.</param>
    /// <param name="offsetFrames">Frames from the extent start to the clip start.</param>
    /// <param name="gainDb">The clip gain in dB.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The expression, for example
    /// <c>if(lt(t,1.000000),-20.000000+(20.000000)*(t-0.000000)/1.000000,0.000000)</c>.</returns>
    public static string Expression(IReadOnlyList<VolumeKey> keys, long offsetFrames, double gainDb, FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("An envelope needs at least one key.", nameof(keys));
        }

        // Stable sort by frame; keys sharing a frame keep only the last one (a vertical step has no ramp to draw).
        var sorted = keys.Select((k, i) => (Key: k, Order: i)).OrderBy(p => p.Key.Frame).ThenBy(p => p.Order).Select(p => p.Key).ToList();
        var points = new List<(long Frame, double Db)>(sorted.Count);
        foreach (var key in sorted)
        {
            var frame = key.Frame + offsetFrames;
            var db = key.Db + gainDb;
            if (points.Count > 0 && points[^1].Frame == frame)
            {
                points[^1] = (frame, db);
            }
            else
            {
                points.Add((frame, db));
            }
        }

        var inner = FfNum.Decimal(points[^1].Db);
        for (var i = points.Count - 2; i >= 0; i--)
        {
            var (f0, g0) = points[i];
            var (f1, g1) = points[i + 1];
            var sb = new StringBuilder();
            sb.Append("if(lt(t,").Append(Seconds(f1, rate)).Append("),")
              .Append(FfNum.Decimal(g0)).Append("+(").Append(FfNum.Decimal(g1 - g0)).Append(")*(t-")
              .Append(Seconds(f0, rate)).Append(")/").Append(Seconds(f1 - f0, rate)).Append(',')
              .Append(inner).Append(')');
            inner = sb.ToString();
        }

        if (points[0].Frame > 0)
        {
            inner = "if(lt(t," + Seconds(points[0].Frame, rate) + ")," + FfNum.Decimal(points[0].Db) + "," + inner + ")";
        }

        return inner;
    }

    private static string Seconds(long frames, FrameRate rate) => FfNum.Seconds(TimeMath.SecondsAt(frames, rate));
}
