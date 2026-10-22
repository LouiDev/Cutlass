using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>Removing time ranges from the timeline (AGENT_INTEGRATION 2.6).</summary>
public static partial class TimelineEdits
{
    /// <summary>
    /// Removes time ranges from the timeline. The ranges are clamped to frame 0, sorted and merged (touching ranges
    /// merge too), then processed last to first: each runs the In/Out ripple delete sequence, a split at both ends, a
    /// lift of what lies between, and with <paramref name="ripple"/> a move left of everything after the range. Works on
    /// the unlocked tracks, or on the unlocked ones among <paramref name="trackIds"/>. Clears In/Out. Labelled
    /// "Remove Ranges". The single range on every unlocked track is the timeline's In/Out ripple delete.
    /// </summary>
    /// <param name="ids">Creates the ids of split-off clips.</param>
    /// <param name="project">The project.</param>
    /// <param name="ranges">The ranges <c>[Start, End)</c> in timeline frames; empty and inverted ones are ignored.</param>
    /// <param name="trackIds">The tracks to cut, or null for every unlocked track. Locked tracks are never cut.</param>
    /// <param name="ripple">True to close each range up; false to leave gaps.</param>
    /// <returns>The result.</returns>
    public static EditResult RemoveRanges(
        IIdGenerator ids,
        Project project,
        IReadOnlyList<(long Start, long End)> ranges,
        IReadOnlyCollection<string>? trackIds,
        bool ripple)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ranges);
        const string label = "Remove Ranges";
        var tracks = project.Sequence.Tracks
            .Where(t => !t.Locked && (trackIds is null || trackIds.Contains(t.Id)))
            .Select(t => t.Id)
            .ToList();
        var merged = MergeRanges(ranges);
        if (merged.Count == 0)
        {
            return EditResult.Unchanged(project, label, "There is no range to remove.");
        }

        if (tracks.Count == 0)
        {
            return EditResult.Unchanged(project, label, "Every track is locked, so nothing was removed.");
        }

        var trackSet = tracks.ToHashSet(StringComparer.Ordinal);
        var warnings = new List<string>();
        var current = project;

        void Step(EditResult r)
        {
            warnings.AddRange(r.Warnings);
            current = r.Project;
        }

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var (start, end) = merged[i];
            Step(Split(ids, current, tracks, start));
            Step(Split(ids, current, tracks, end));
            var inside = ClipsOn(current, trackSet).Where(c => c.Start >= start && c.End <= end).Select(c => c.Id).ToList();
            if (inside.Count > 0)
            {
                Step(Lift(current, inside));
            }

            if (!ripple)
            {
                continue;
            }

            // The range is empty on these tracks now, so the block move left cannot cover anything. RippleShift gives
            // exactly what Move would for these clips, in linear instead of quadratic time (2.3: 50 ms over 1000 clips).
            Step(RippleShift(ids, current, trackSet, end, start - end));
        }

        Step(SetInOut(current, null, null));
        return ReferenceEquals(current, project)
            ? EditResult.Unchanged(project, label, [.. warnings.Distinct(StringComparer.Ordinal)])
            : new EditResult(current, label, [.. warnings.Distinct(StringComparer.Ordinal)], ImmutableArray<string>.Empty);
    }

    /// <summary>Clamps ranges to frame 0, drops empty ones, sorts them and merges overlapping or touching ones.</summary>
    /// <param name="ranges">The ranges.</param>
    /// <returns>The merged ranges, sorted by start.</returns>
    public static IReadOnlyList<(long Start, long End)> MergeRanges(IReadOnlyList<(long Start, long End)> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        var sorted = ranges
            .Select(r => (Start: Math.Max(0, r.Start), r.End))
            .Where(r => r.End > r.Start)
            .OrderBy(r => r.Start)
            .ThenBy(r => r.End)
            .ToList();
        var merged = new List<(long Start, long End)>(sorted.Count);
        foreach (var r in sorted)
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, r.End));
            }
            else
            {
                merged.Add(r);
            }
        }

        return merged;
    }

    // Move(clips starting at or after from, delta, 0, Overwrite) for every clip of the given unlocked tracks, when nothing
    // lies in the vacated span: the clips shift, and transitions between two moved clips travel with them (re-appended,
    // as Move does, so the result is the same).
    private static EditResult RippleShift(IIdGenerator ids, Project project, HashSet<string> trackIds, long from, long delta)
    {
        const string label = "Move";
        var w = new Work(project, ids);
        var moved = new List<string>();
        foreach (var track in w.Tracks)
        {
            if (track.Locked || !trackIds.Contains(track.Id))
            {
                continue;
            }

            var first = track.Clips.FindIndex(c => c.Start >= from);
            if (first < 0)
            {
                continue;
            }

            var movedHere = new HashSet<string>(StringComparer.Ordinal);
            for (var i = first; i < track.Clips.Count; i++)
            {
                var clip = track.Clips[i];
                movedHere.Add(clip.Id);
                moved.Add(clip.Id);
                track.Clips[i] = clip with { Start = clip.Start + delta };
            }

            var carried = track.Transitions.Where(t => movedHere.Contains(t.LeftClipId) && movedHere.Contains(t.RightClipId)).ToList();
            if (carried.Count > 0)
            {
                track.Transitions.RemoveAll(t => movedHere.Contains(t.LeftClipId) && movedHere.Contains(t.RightClipId));
                track.Transitions.AddRange(carried);
            }
        }

        return moved.Count == 0 ? EditResult.Unchanged(project, label) : w.Finish(label, moved);
    }

    private static IEnumerable<Clip> ClipsOn(Project project, HashSet<string> trackIds) =>
        project.Sequence.Tracks.Where(t => trackIds.Contains(t.Id)).SelectMany(t => t.Clips);
}
