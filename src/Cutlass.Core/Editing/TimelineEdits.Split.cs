using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>Razor splits.</summary>
public static partial class TimelineEdits
{
    /// <summary>
    /// Splits, at <paramref name="frame"/>, the clip that strictly contains it on each listed unlocked track (only the
    /// listed clips when <paramref name="onlyClipIds"/> is given). Splitting at a clip boundary does nothing.
    /// </summary>
    /// <remarks>
    /// The left half keeps the clip id and link group; the right halves get new ids and, per original group, one new
    /// link group, so left halves stay together and right halves stay together (DESIGN 5.5). Fades at the new cut
    /// are dropped, volume keyframes are split with a boundary key on each side, and an outgoing transition moves to
    /// the right half.
    /// </remarks>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="trackIds">The tracks to split.</param>
    /// <param name="frame">The split frame.</param>
    /// <param name="onlyClipIds">Restricts the split to these clips (the selection), or null.</param>
    /// <returns>The result.</returns>
    public static EditResult Split(
        IIdGenerator ids, Project project, IReadOnlyCollection<string> trackIds, long frame, IReadOnlyCollection<string>? onlyClipIds = null)
    {
        const string label = "Split";
        var w = new Work(project, ids);
        var any = false;
        foreach (var track in w.Tracks)
        {
            if (!trackIds.Contains(track.Id))
            {
                continue;
            }

            var index = track.Clips.FindIndex(c => c.Start < frame && frame < c.End);
            if (index < 0 || (onlyClipIds is not null && !onlyClipIds.Contains(track.Clips[index].Id)))
            {
                continue;
            }

            if (track.Locked)
            {
                w.Warn($"{track.Name} is locked.");
                continue;
            }

            w.SplitAt(track, index, frame);
            any = true;
        }

        return any ? w.Finish(label) : EditResult.Unchanged(project, label, [.. w.Warnings]);
    }

    /// <summary>
    /// Razor click: splits a clip and its link partners at <paramref name="frame"/> (Shift+click passes every unlocked
    /// track to <see cref="Split"/> instead).
    /// </summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The clicked clip.</param>
    /// <param name="frame">The split frame.</param>
    /// <returns>The result.</returns>
    public static EditResult SplitLinked(IIdGenerator ids, Project project, string clipId, long frame)
    {
        var group = TimelineQuery.LinkGroup(project, clipId);
        var tracks = project.Sequence.Tracks.Select(t => t.Id).ToList();
        return Split(ids, project, tracks, frame, group);
    }
}
