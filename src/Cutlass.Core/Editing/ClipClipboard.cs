using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>
/// Copied clips (Ctrl+C, Ctrl+X) with their tracks and offsets relative to the earliest copied clip. Pasting places
/// them at the playhead on the same tracks (Ctrl+V overwrite, Ctrl+Shift+V insert) with new ids and link groups.
/// Transitions are not copied (simplest consistent choice: pasted clips start as plain cuts).
/// </summary>
/// <param name="Items">The copied clips; each clip's start is its offset from the earliest copied clip.</param>
public sealed record ClipClipboard(ImmutableArray<ClipPlacement> Items)
{
    /// <summary>An empty clipboard.</summary>
    public static ClipClipboard Empty { get; } = new(ImmutableArray<ClipPlacement>.Empty);

    /// <summary>True when nothing is copied.</summary>
    public bool IsEmpty => Items.IsDefaultOrEmpty;

    /// <summary>Copies clips (in track order, then time order) from a project.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The clips to copy.</param>
    /// <returns>The clipboard (empty when no id exists).</returns>
    public static ClipClipboard Copy(Project project, IEnumerable<string> clipIds)
    {
        var wanted = clipIds.ToHashSet(StringComparer.Ordinal);
        var found = new List<(string TrackId, Clip Clip)>();
        foreach (var track in project.Sequence.Tracks)
        {
            foreach (var clip in track.Clips)
            {
                if (wanted.Contains(clip.Id))
                {
                    found.Add((track.Id, clip));
                }
            }
        }

        if (found.Count == 0)
        {
            return Empty;
        }

        var origin = found.Min(f => f.Clip.Start);
        return new ClipClipboard([.. found.Select(f => new ClipPlacement(f.TrackId, f.Clip with { Start = f.Clip.Start - origin }))]);
    }

    /// <summary>
    /// Maps the copied clips onto a project's tracks: the same track when it still exists, else the first unlocked
    /// track of the right kind.
    /// </summary>
    /// <param name="project">The project to paste into.</param>
    /// <returns>Placements for <see cref="TimelineEdits.Place"/>.</returns>
    public ImmutableArray<ClipPlacement> ResolvePlacements(Project project)
    {
        if (IsEmpty)
        {
            return ImmutableArray<ClipPlacement>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<ClipPlacement>();
        foreach (var item in Items)
        {
            if (TimelineQuery.FindTrack(project, item.TrackId) is not null)
            {
                builder.Add(item);
                continue;
            }

            var kind = item.Clip is MediaClip { Stream: StreamKind.Audio } ? TrackKind.Audio : TrackKind.Video;
            var fallback = project.Sequence.Tracks.FirstOrDefault(t => t.Kind == kind && !t.Locked);
            if (fallback is not null)
            {
                builder.Add(item with { TrackId = fallback.Id });
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>Pastes at <paramref name="at"/> with new ids and link groups.</summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="at">The paste frame (the playhead).</param>
    /// <param name="mode">Overwrite (Ctrl+V) or insert (Ctrl+Shift+V).</param>
    /// <returns>The result, labelled "Paste"; <see cref="EditResult.Select"/> holds the pasted clip ids.</returns>
    public EditResult Paste(IIdGenerator ids, Project project, long at, EditMode mode)
    {
        if (IsEmpty)
        {
            return EditResult.Unchanged(project, "Paste");
        }

        return TimelineEdits.Place(ids, project, ResolvePlacements(project), at, mode) with { Label = "Paste" };
    }
}
