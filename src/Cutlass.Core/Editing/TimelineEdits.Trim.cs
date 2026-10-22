using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Core.Editing;

/// <summary>Trims, ripple trims, rolls and slips.</summary>
public static partial class TimelineEdits
{
    /// <summary>
    /// Trims one edge of a clip by <paramref name="delta"/> frames (positive moves the edge right).
    /// </summary>
    /// <remarks>
    /// <para>A plain trim stops at the neighbour and the source limits and leaves a gap when shortening.</para>
    /// <para>
    /// A ripple trim ignores neighbours: an end trim moves every later clip on the track by <c>delta</c>; a start trim
    /// keeps the clip's start, changes its source in point and moves later clips by <c>-delta</c>. Every other unlocked
    /// track shifts its clips at or after the edit point the same way; a track without room to close up is left alone.
    /// </para>
    /// <para>
    /// With <paramref name="includeLinked"/>, link partners whose same edge sits on the same frame are trimmed too, by
    /// the same clamped delta (Alt at press time passes false, which makes J and L cuts).
    /// </para>
    /// </remarks>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The clip to trim.</param>
    /// <param name="edge">The edge to move.</param>
    /// <param name="delta">The requested edge movement in frames.</param>
    /// <param name="ripple">True for a ripple trim.</param>
    /// <param name="includeLinked">True to trim link partners whose edge is on the same frame.</param>
    /// <returns>The result.</returns>
    public static EditResult Trim(Project project, string clipId, ClipEdge edge, long delta, bool ripple, bool includeLinked)
    {
        var label = ripple ? "Ripple Trim" : "Trim";
        var w = new Work(project);
        if (w.Find(clipId) is not { } found)
        {
            return EditResult.Unchanged(project, label, "The clip no longer exists.");
        }

        if (found.Track.Locked)
        {
            return EditResult.Unchanged(project, label, $"{found.Track.Name} is locked.");
        }

        var primary = found.Track.Clips[found.Index];
        var edgeFrame = edge == ClipEdge.Start ? primary.Start : primary.End;
        var targets = new List<(TrackWork Track, Clip Clip)> { (found.Track, primary) };
        if (includeLinked && primary.LinkId is not null)
        {
            foreach (var track in w.Tracks)
            {
                if (track.Locked || ReferenceEquals(track, found.Track))
                {
                    continue;
                }

                var partner = track.Clips.Find(c => c.LinkId == primary.LinkId
                    && (edge == ClipEdge.Start ? c.Start : c.End) == edgeFrame);
                if (partner is not null)
                {
                    targets.Add((track, partner));
                }
            }
        }

        long lo = long.MinValue, hi = long.MaxValue;
        string loReason = string.Empty, hiReason = string.Empty;
        foreach (var (track, clip) in targets)
        {
            var (l, h, lr, hr) = TrimRange(w, track, clip, edge, ripple);
            if (l > lo)
            {
                lo = l;
                loReason = lr;
            }

            if (h < hi)
            {
                hi = h;
                hiReason = hr;
            }
        }

        if (lo > hi)
        {
            return EditResult.Unchanged(project, label, "The clip cannot be trimmed here.");
        }

        var d = Math.Clamp(delta, lo, hi);
        if (d != delta)
        {
            w.Warn(d < delta ? hiReason : loReason);
        }

        if (d == 0)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        foreach (var (track, clip) in targets)
        {
            var index = track.IndexOf(clip.Id);
            if (edge == ClipEdge.End)
            {
                track.Clips[index] = Work.TrimTailTo(clip, clip.End + d);
                if (ripple)
                {
                    ShiftAfter(track, index, d);
                }
            }
            else if (ripple)
            {
                track.Clips[index] = w.TrimHeadTo(clip, clip.Start + d) with { Start = clip.Start };
                ShiftAfter(track, index, -d);
            }
            else
            {
                track.Clips[index] = w.TrimHeadTo(clip, clip.Start + d);
            }
        }

        if (ripple)
        {
            w.RippleOthers([.. targets.Select(t => t.Track)], edgeFrame, edge == ClipEdge.End ? d : -d);
        }

        return w.Finish(label);
    }

    /// <summary>
    /// Ripple trims to the playhead (Q and W): on the first listed unlocked track with a clip under
    /// <paramref name="frame"/>, Q (<see cref="ClipEdge.Start"/>) removes from the clip's start to the frame and W
    /// (<see cref="ClipEdge.End"/>) removes from the frame to the clip's end, rippling like <see cref="Trim"/> with
    /// linked partners included.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="trackIds">The tracks to consider in priority order (typically the targeted tracks).</param>
    /// <param name="frame">The playhead frame.</param>
    /// <param name="edge">Start for Q, End for W.</param>
    /// <returns>The result.</returns>
    public static EditResult RippleTrimToFrame(Project project, IReadOnlyList<string> trackIds, long frame, ClipEdge edge)
    {
        var label = edge == ClipEdge.Start ? "Ripple Trim Start" : "Ripple Trim End";
        foreach (var trackId in trackIds)
        {
            var track = TimelineQuery.FindTrack(project, trackId);
            if (track is null || track.Locked || TimelineQuery.ClipAt(track, frame) is not { } clip)
            {
                continue;
            }

            var delta = edge == ClipEdge.Start ? frame - clip.Start : frame - clip.End;
            if (delta == 0)
            {
                return EditResult.Unchanged(project, label);
            }

            return Trim(project, clip.Id, edge, delta, ripple: true, includeLinked: true) with { Label = label };
        }

        return EditResult.Unchanged(project, label, "There is no clip under the playhead on the targeted tracks.");
    }

    /// <summary>
    /// Rolls the cut between two butting clips by <paramref name="delta"/> frames: the left clip's end and the right
    /// clip's start move together. Limited by both source handles and a one-frame minimum on each side. With
    /// <paramref name="includeLinked"/>, butting link partners on other tracks that share the same cut roll too.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="leftId">The outgoing clip.</param>
    /// <param name="rightId">The incoming clip.</param>
    /// <param name="delta">The cut movement in frames.</param>
    /// <param name="includeLinked">True to roll matching linked cuts on other tracks.</param>
    /// <returns>The result.</returns>
    public static EditResult Roll(Project project, string leftId, string rightId, long delta, bool includeLinked = false)
    {
        const string label = "Roll";
        var w = new Work(project);
        if (w.Find(leftId) is not { } lf || w.Find(rightId) is not { } rf || !ReferenceEquals(lf.Track, rf.Track)
            || rf.Index != lf.Index + 1 || lf.Track.Clips[lf.Index].End != rf.Track.Clips[rf.Index].Start)
        {
            return EditResult.Unchanged(project, label, "Roll needs two butting clips on one track.");
        }

        if (lf.Track.Locked)
        {
            return EditResult.Unchanged(project, label, $"{lf.Track.Name} is locked.");
        }

        var left = lf.Track.Clips[lf.Index];
        var right = rf.Track.Clips[rf.Index];
        var pairs = new List<(TrackWork Track, Clip Left, Clip Right)> { (lf.Track, left, right) };
        if (includeLinked)
        {
            foreach (var track in w.Tracks)
            {
                if (track.Locked || ReferenceEquals(track, lf.Track))
                {
                    continue;
                }

                var i = track.Clips.FindIndex(c => c.End == left.End && c.LinkId is not null && c.LinkId == left.LinkId);
                if (i >= 0 && i + 1 < track.Clips.Count && track.Clips[i + 1].Start == left.End
                    && track.Clips[i + 1].LinkId is not null && track.Clips[i + 1].LinkId == right.LinkId)
                {
                    pairs.Add((track, track.Clips[i], track.Clips[i + 1]));
                }
            }
        }

        long lo = long.MinValue, hi = long.MaxValue;
        foreach (var (_, l, r) in pairs)
        {
            lo = Math.Max(lo, Math.Max(1 - l.Duration, -w.Head(r)));
            hi = Math.Min(hi, Math.Min(w.Tail(l), r.Duration - 1));
        }

        if (lo > hi)
        {
            return EditResult.Unchanged(project, label, "This cut cannot roll.");
        }

        var d = Math.Clamp(delta, lo, hi);
        if (d != delta)
        {
            w.Warn("Roll stopped at the limit of the media or the clip.");
        }

        if (d == 0)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        foreach (var (track, l, r) in pairs)
        {
            track.Clips[track.IndexOf(l.Id)] = Work.TrimTailTo(l, l.End + d);
            track.Clips[track.IndexOf(r.Id)] = w.TrimHeadTo(r, r.Start + d);
        }

        return w.Finish(label);
    }

    /// <summary>
    /// Slips a clip's source by <paramref name="delta"/> timeline frames (positive shows later source); the clip never
    /// moves or resizes. Limited so the clip's extent, including transition handles, stays inside the source. With
    /// <paramref name="includeLinked"/>, every slippable link partner slips by the same amount. Titles and stills have
    /// no source to slip.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The clip.</param>
    /// <param name="delta">The source offset in timeline frames at the clip's speed.</param>
    /// <param name="includeLinked">True to slip link partners as well.</param>
    /// <returns>The result.</returns>
    public static EditResult Slip(Project project, string clipId, long delta, bool includeLinked)
    {
        const string label = "Slip";
        var w = new Work(project);
        if (w.Find(clipId) is not { } found)
        {
            return EditResult.Unchanged(project, label, "The clip no longer exists.");
        }

        if (found.Track.Locked)
        {
            return EditResult.Unchanged(project, label, $"{found.Track.Name} is locked.");
        }

        var primary = found.Track.Clips[found.Index];
        if (primary is not MediaClip || w.IsStill(primary))
        {
            return EditResult.Unchanged(project, label, "Titles and stills have no source to slip.");
        }

        var targets = new List<(TrackWork Track, MediaClip Clip)> { (found.Track, (MediaClip)primary) };
        if (includeLinked && primary.LinkId is not null)
        {
            foreach (var track in w.Tracks)
            {
                if (track.Locked)
                {
                    continue;
                }

                foreach (var c in track.Clips)
                {
                    if (c is MediaClip m && m.Id != primary.Id && m.LinkId == primary.LinkId && !w.IsStill(m))
                    {
                        targets.Add((track, m));
                    }
                }
            }
        }

        long lo = long.MinValue, hi = long.MaxValue;
        foreach (var (track, clip) in targets)
        {
            var (head, tail) = Extent(track, clip);
            lo = Math.Max(lo, head - w.Head(clip));
            hi = Math.Min(hi, w.Tail(clip) - tail);
        }

        if (lo > hi)
        {
            return EditResult.Unchanged(project, label, "This clip has no room to slip.");
        }

        var d = Math.Clamp(delta, lo, hi);
        if (d != delta)
        {
            w.Warn("Slip stopped at the limit of the source media.");
        }

        // At speeds other than 100 % the source offset is floored, so step back until every extent fits exactly.
        while (d != 0)
        {
            var slipped = targets.Select(t => (t.Track, Clip: SlipClip(t.Clip, d, w.Rate))).ToList();
            if (slipped.TrueForAll(t => FitsSource(w, t.Track, t.Clip)))
            {
                foreach (var (track, clip) in slipped)
                {
                    track.Clips[track.IndexOf(clip.Id)] = clip;
                }

                return w.Finish(label);
            }

            d -= Math.Sign(d);
        }

        return EditResult.Unchanged(project, label, [.. w.Warnings]);

        static bool FitsSource(Work w, TrackWork track, MediaClip clip)
        {
            var (head, tail) = Extent(track, clip);
            return w.Head(clip) >= head && w.Tail(clip) >= tail;
        }
    }

    private static MediaClip SlipClip(MediaClip clip, long frames, FrameRate rate) =>
        clip with
        {
            SourceInFlicks = clip.SourceInFlicks
                + (long)TimeMath.FloorDiv((Int128)frames * rate.FlicksPerFrame * clip.SpeedPercent, 100),
        };

    private static (long Head, long Tail) Extent(TrackWork track, Clip clip)
    {
        long head = 0, tail = 0;
        foreach (var transition in track.Transitions)
        {
            var (a, b) = TimelineQuery.TransitionHalves(transition.Duration);
            if (transition.RightClipId == clip.Id)
            {
                head = a;
            }

            if (transition.LeftClipId == clip.Id)
            {
                tail = b;
            }
        }

        return (head, tail);
    }

    private static void ShiftAfter(TrackWork track, int index, long delta)
    {
        for (var i = index + 1; i < track.Clips.Count; i++)
        {
            track.Clips[i] = track.Clips[i] with { Start = track.Clips[i].Start + delta };
        }
    }

    private static (long Lo, long Hi, string LoReason, string HiReason) TrimRange(
        Work w, TrackWork track, Clip clip, ClipEdge edge, bool ripple)
    {
        const string minLength = "A clip must be at least one frame long.";
        const string sourceEnd = "Reached the end of the source media.";
        const string sourceStart = "Reached the start of the source media.";
        var index = track.IndexOf(clip.Id);
        if (edge == ClipEdge.End)
        {
            var tail = w.Tail(clip);
            if (ripple || index + 1 >= track.Clips.Count)
            {
                return (1 - clip.Duration, tail, minLength, sourceEnd);
            }

            var room = track.Clips[index + 1].Start - clip.End;
            return room < tail
                ? (1 - clip.Duration, room, minLength, "Reached the next clip.")
                : (1 - clip.Duration, tail, minLength, sourceEnd);
        }

        var head = w.Head(clip);
        if (ripple)
        {
            return (-head, clip.Duration - 1, sourceStart, minLength);
        }

        var previousEnd = index > 0 ? track.Clips[index - 1].End : 0;
        var space = clip.Start - previousEnd;
        return space < head
            ? (-space, clip.Duration - 1, index > 0 ? "Reached the previous clip." : "Reached the start of the timeline.", minLength)
            : (-head, clip.Duration - 1, sourceStart, minLength);
    }
}
