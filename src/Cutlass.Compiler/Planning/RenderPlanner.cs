using System.Collections.Immutable;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Compiler.Planning;

/// <summary>
/// Decides which parts of which clips a window needs (TECH_BRIEF 7.4 to 7.6). Pure; the emitters turn the plans into
/// filter chains.
/// </summary>
public static class RenderPlanner
{
    /// <summary>
    /// Plans a video track: its runs that intersect <c>[windowStart, windowEnd)</c>, each widened outward when an end
    /// falls strictly inside one of its transition regions, with the segments the widened interval needs.
    /// </summary>
    /// <param name="track">The video track.</param>
    /// <param name="windowStart">The first frame of the window.</param>
    /// <param name="windowEnd">The end of the window (exclusive).</param>
    /// <returns>The runs in timeline order.</returns>
    public static ImmutableArray<PlannedRun> PlanVideoTrack(Track track, long windowStart, long windowEnd)
    {
        ArgumentNullException.ThrowIfNull(track);
        var result = ImmutableArray.CreateBuilder<PlannedRun>();
        foreach (var run in TimelineQuery.Runs(track))
        {
            var visibleStart = Math.Max(run[0].Start, windowStart);
            var visibleEnd = Math.Min(run[^1].End, windowEnd);
            if (visibleStart >= visibleEnd)
            {
                continue;
            }

            var joins = JoinsOf(track, run);
            long renderStart = visibleStart, renderEnd = visibleEnd;
            for (var i = 0; i < joins.Length; i++)
            {
                if (joins[i] is not { } transition)
                {
                    continue;
                }

                var (regionStart, regionEnd) = TransitionRegion(transition, run[i].End);
                if (regionStart < renderStart && renderStart < regionEnd)
                {
                    renderStart = regionStart;
                }

                if (regionStart < renderEnd && renderEnd < regionEnd)
                {
                    renderEnd = regionEnd;
                }
            }

            var segments = ImmutableArray.CreateBuilder<PlannedSegment>();
            var segmentJoins = ImmutableArray.CreateBuilder<Transition?>();
            var previous = -1;
            for (var i = 0; i < run.Length; i++)
            {
                var segment = Segment(run, joins, i, renderStart, renderEnd);
                if (segment is null)
                {
                    continue;
                }

                if (previous >= 0)
                {
                    if (previous != i - 1)
                    {
                        throw new InvalidOperationException("The segments of a run must be contiguous.");
                    }

                    segmentJoins.Add(joins[i - 1]);
                }

                segments.Add(segment);
                previous = i;
            }

            result.Add(new PlannedRun(
                segments.ToImmutable(),
                segmentJoins.ToImmutable(),
                renderStart,
                renderEnd,
                visibleStart,
                visibleEnd,
                run.Length,
                joins.Any(j => j is not null)));
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Plans an audio track: every enabled clip whose extent intersects the window, cut to the window. Audio segments
    /// are placed independently with <c>adelay</c>, so no widening is needed.
    /// </summary>
    /// <param name="track">The audio track.</param>
    /// <param name="windowStart">The first frame of the window.</param>
    /// <param name="windowEnd">The end of the window (exclusive).</param>
    /// <returns>The segments in timeline order.</returns>
    public static ImmutableArray<PlannedSegment> PlanAudioTrack(Track track, long windowStart, long windowEnd)
    {
        ArgumentNullException.ThrowIfNull(track);
        var result = ImmutableArray.CreateBuilder<PlannedSegment>();
        foreach (var run in TimelineQuery.Runs(track))
        {
            var joins = JoinsOf(track, run);
            for (var i = 0; i < run.Length; i++)
            {
                if (Segment(run, joins, i, windowStart, windowEnd) is { } segment)
                {
                    result.Add(segment);
                }
            }
        }

        return result.ToImmutable();
    }

    /// <summary>The frames a transition covers: <c>[cut - a, cut + b)</c> with <c>a = d/2</c> and <c>b = d - a</c>.</summary>
    /// <param name="transition">The transition.</param>
    /// <param name="cut">The cut frame (the left clip's end).</param>
    /// <returns>The region.</returns>
    public static (long Start, long End) TransitionRegion(Transition transition, long cut)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var (a, b) = TimelineQuery.TransitionHalves(transition.Duration);
        return (cut - a, cut + b);
    }

    private static PlannedSegment? Segment(
        ImmutableArray<Clip> run,
        ImmutableArray<Transition?> joins,
        int i,
        long intervalStart,
        long intervalEnd)
    {
        var clip = run[i];
        var head = i > 0 ? joins[i - 1] : null;
        var tail = i < joins.Length ? joins[i] : null;
        var extentStart = clip.Start - (head is null ? 0 : TimelineQuery.TransitionHalves(head.Duration).A);
        var extentEnd = clip.End + (tail is null ? 0 : TimelineQuery.TransitionHalves(tail.Duration).B);
        var start = Math.Max(extentStart, intervalStart);
        var end = Math.Min(extentEnd, intervalEnd);
        return start < end ? new PlannedSegment(clip, extentStart, extentEnd, start, end, head, tail) : null;
    }

    // The transition joining each pair of consecutive clips of a run, or null for a plain cut. Transitions that break
    // the validator's geometry (d < 2, a half longer than its clip) are treated as cuts so the plan stays consistent.
    private static ImmutableArray<Transition?> JoinsOf(Track track, ImmutableArray<Clip> run)
    {
        var joins = ImmutableArray.CreateBuilder<Transition?>(Math.Max(0, run.Length - 1));
        for (var i = 0; i + 1 < run.Length; i++)
        {
            Transition? found = null;
            foreach (var transition in track.Transitions)
            {
                if (transition.LeftClipId != run[i].Id || transition.RightClipId != run[i + 1].Id)
                {
                    continue;
                }

                var (a, b) = TimelineQuery.TransitionHalves(transition.Duration);
                if (transition.Duration >= 2 && a <= run[i].Duration && b <= run[i + 1].Duration)
                {
                    found = transition;
                }

                break;
            }

            joins.Add(found);
        }

        return joins.MoveToImmutable();
    }
}
