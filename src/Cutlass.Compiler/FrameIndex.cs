using System.Collections.Immutable;
using Cutlass.Core.Time;

namespace Cutlass.Compiler;

/// <summary>
/// The presentation timestamps of every decodable frame of a video stream, ascending and distinct (TECH_BRIEF 6.4).
/// Ordinal <c>i</c> is the i-th frame in presentation order. Seeks pick an ordinal from the original's index and that
/// ordinal's timestamp from the file actually read (TECH_BRIEF 6.6 and 7.2).
/// </summary>
/// <remarks>
/// Timestamps are raw stream values in <see cref="TimeBase"/> units, including any start offset, so they compare
/// directly with <c>format.start_time + source position</c>. All comparisons use exact 128-bit integer math.
/// </remarks>
public sealed class FrameIndex
{
    private readonly long[] _pts;

    /// <summary>Creates an index.</summary>
    /// <param name="timeBase">The stream time base, for example 1/12800. Must be positive.</param>
    /// <param name="pts">The timestamps, strictly ascending. Must not be empty.</param>
    /// <exception cref="ArgumentException">When the time base is not positive, the list is empty or not strictly
    /// ascending.</exception>
    public FrameIndex(Rational timeBase, IEnumerable<long> pts)
    {
        ArgumentNullException.ThrowIfNull(pts);
        if (!timeBase.IsPositive)
        {
            throw new ArgumentException("The time base of a frame index must be positive.", nameof(timeBase));
        }

        _pts = [.. pts];
        if (_pts.Length == 0)
        {
            throw new ArgumentException("A frame index needs at least one timestamp.", nameof(pts));
        }

        for (var i = 1; i < _pts.Length; i++)
        {
            if (_pts[i] <= _pts[i - 1])
            {
                throw new ArgumentException("Frame index timestamps must be strictly ascending.", nameof(pts));
            }
        }

        TimeBase = timeBase;
    }

    /// <summary>The stream time base.</summary>
    public Rational TimeBase { get; }

    /// <summary>The number of frames.</summary>
    public int Count => _pts.Length;

    /// <summary>The timestamps in <see cref="TimeBase"/> units, ascending.</summary>
    public ImmutableArray<long> Pts => [.. _pts];

    /// <summary>The timestamp of an ordinal in <see cref="TimeBase"/> units.</summary>
    /// <param name="ordinal">The ordinal, 0 to <see cref="Count"/> - 1.</param>
    /// <returns>The raw timestamp.</returns>
    public long this[int ordinal] => _pts[ordinal];

    /// <summary>
    /// The largest ordinal whose timestamp is at or before <paramref name="flicks"/> (an absolute stream time), or 0
    /// when every frame is later (sample-and-hold shows the first frame).
    /// </summary>
    /// <param name="flicks">The absolute time in flicks (<c>format.start_time</c> plus the source position).</param>
    /// <returns>The ordinal.</returns>
    public int FloorOrdinal(long flicks)
    {
        // pts * tbNum / tbDen <= flicks / F  <=>  pts * tbNum * F <= flicks * tbDen (tbDen > 0).
        var target = (Int128)flicks * TimeBase.Den;
        int lo = 0, hi = _pts.Length - 1, found = 0;
        while (lo <= hi)
        {
            var mid = (lo + hi) >>> 1;
            if ((Int128)_pts[mid] * TimeBase.Num * TimeMath.FlicksPerSecond <= target)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    /// <summary>The time of an ordinal in flicks, floored.</summary>
    /// <param name="ordinal">The ordinal.</param>
    /// <returns><c>floor(pts * TimeBase * FlicksPerSecond)</c>.</returns>
    public long FlicksAt(int ordinal) => TimeMath.FlicksFromTicks(_pts[ordinal], TimeBase);

    /// <summary>The time of an ordinal in microseconds, floored (seeks are floored to the microsecond).</summary>
    /// <param name="ordinal">The ordinal.</param>
    /// <returns><c>floor(pts * TimeBase * 1,000,000)</c>.</returns>
    public long FloorMicrosAt(int ordinal) =>
        (long)TimeMath.FloorDiv((Int128)_pts[ordinal] * TimeBase.Num * TimeMath.MicrosPerSecond, TimeBase.Den);
}
