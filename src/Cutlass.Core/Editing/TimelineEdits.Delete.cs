using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>Lift, ripple delete and closing gaps.</summary>
public static partial class TimelineEdits
{
    /// <summary>Removes clips and leaves gaps (Delete, Backspace). Clips on locked tracks stay.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips to remove.</param>
    /// <returns>The result.</returns>
    public static EditResult Lift(Project project, IReadOnlyCollection<string> clipIds)
    {
        const string label = "Lift";
        var w = new Work(project);
        return RemoveClips(w, clipIds).Count == 0
            ? EditResult.Unchanged(project, label, [.. w.Warnings])
            : w.Finish(label);
    }

    /// <summary>
    /// Removes clips and closes the space they leave (Shift+Delete). Each track that lost clips closes its own merged
    /// ranges. Every other unlocked track closes the ranges of the first track (in track order) that lost clips, but
    /// only where it has room: a track with material in a range is left alone for that range.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips to remove.</param>
    /// <returns>The result.</returns>
    public static EditResult RippleDelete(Project project, IReadOnlyCollection<string> clipIds)
    {
        const string label = "Ripple Delete";
        var w = new Work(project);
        var removed = RemoveClips(w, clipIds);
        if (removed.Count == 0)
        {
            return EditResult.Unchanged(project, label, [.. w.Warnings]);
        }

        var ranges = new Dictionary<TrackWork, List<(long Start, long End)>>();
        foreach (var (track, clip) in removed)
        {
            if (!ranges.TryGetValue(track, out var list))
            {
                list = [];
                ranges[track] = list;
            }

            list.Add((clip.Start, clip.End));
        }

        foreach (var key in ranges.Keys.ToList())
        {
            ranges[key] = Merge(ranges[key]);
        }

        var reference = w.Tracks.First(ranges.ContainsKey);
        foreach (var track in w.Tracks)
        {
            if (track.Locked)
            {
                continue;
            }

            var own = ranges.TryGetValue(track, out var list);
            foreach (var (start, end) in (own ? list! : ranges[reference]).OrderByDescending(r => r.Start))
            {
                if (track.Overlaps(start, end))
                {
                    w.Warn($"{track.Name} has no room to close up and was left alone.");
                    continue;
                }

                track.ShiftFrom(end, start - end);
            }
        }

        return w.Finish(label);
    }

    /// <summary>
    /// Closes the gap of a track that contains <paramref name="gapStart"/> (a selected gap with Delete, Shift+Delete or
    /// "Close gap"). Later clips on that track move left by the gap length, and every other unlocked track with room in
    /// the same range moves with it; tracks without room are left alone.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="trackId">The track of the gap.</param>
    /// <param name="gapStart">A frame inside the gap, normally its first frame.</param>
    /// <returns>The result.</returns>
    public static EditResult CloseGap(Project project, string trackId, long gapStart)
    {
        const string label = "Close Gap";
        var source = TimelineQuery.FindTrack(project, trackId);
        if (source is null)
        {
            return EditResult.Unchanged(project, label, "The track no longer exists.");
        }

        if (source.Locked)
        {
            return EditResult.Unchanged(project, label, $"{source.Name} is locked.");
        }

        if (TimelineQuery.GapAt(source, gapStart) is not { } gap)
        {
            return EditResult.Unchanged(project, label, "There is no gap there.");
        }

        var w = new Work(project);
        foreach (var track in w.Tracks)
        {
            if (track.Locked)
            {
                continue;
            }

            if (track.Overlaps(gap.Start, gap.End))
            {
                w.Warn($"{track.Name} has no room to close up and was left alone.");
                continue;
            }

            track.ShiftFrom(gap.End, gap.Start - gap.End);
        }

        return w.Finish(label);
    }

    private static List<(TrackWork Track, Clip Clip)> RemoveClips(Work w, IReadOnlyCollection<string> clipIds)
    {
        var removed = new List<(TrackWork, Clip)>();
        foreach (var id in clipIds.Distinct(StringComparer.Ordinal))
        {
            if (w.Find(id) is not { } found)
            {
                continue;
            }

            if (found.Track.Locked)
            {
                w.Warn($"{found.Track.Name} is locked.");
                continue;
            }

            removed.Add((found.Track, found.Track.Clips[found.Index]));
            found.Track.Clips.RemoveAt(found.Index);
        }

        return removed;
    }

    private static List<(long Start, long End)> Merge(List<(long Start, long End)> ranges)
    {
        var merged = new List<(long Start, long End)>();
        foreach (var r in ranges.OrderBy(r => r.Start))
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
}
