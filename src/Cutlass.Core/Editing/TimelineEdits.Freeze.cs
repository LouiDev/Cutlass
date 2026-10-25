using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Core.Editing;

/// <summary>Freeze frames (effects batch 4, DESIGN 5.5): holding one frame of a video clip for a duration.</summary>
public static partial class TimelineEdits
{
    /// <summary>
    /// Holds the frame a video clip shows at <paramref name="frame"/> for <paramref name="durationFrames"/>: the clip
    /// is split at <paramref name="frame"/> together with its link partners (no split when the frame is the clip start)
    /// and a copy of it with <see cref="MediaClip.Freeze"/> set, the source position of that frame (speed included),
    /// the same video properties without fades and no link is placed at <paramref name="frame"/> on the same track. With
    /// <paramref name="ripple"/> the copy is inserted and the following clips on every unlocked track move, like any
    /// insert edit; without it the copy overwrites what follows. The duration is at least one frame.
    /// </summary>
    /// <param name="ids">The id generator.</param>
    /// <param name="project">The project.</param>
    /// <param name="clipId">The video clip.</param>
    /// <param name="frame">The timeline frame to hold; must lie inside the clip.</param>
    /// <param name="durationFrames">The hold length in frames.</param>
    /// <param name="ripple">True to insert, false to overwrite.</param>
    /// <returns>The result, labelled "Freeze Frame"; <see cref="EditResult.Select"/> holds the frozen clip's id.</returns>
    public static EditResult FreezeFrame(IIdGenerator ids, Project project, string clipId, long frame, long durationFrames, bool ripple)
    {
        const string label = "Freeze Frame";
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(project);
        if (TimelineQuery.FindClip(project, clipId) is not { } found)
        {
            return EditResult.Unchanged(project, label, "The clip no longer exists.");
        }

        if (found.Track.Locked)
        {
            return EditResult.Unchanged(project, label, $"{found.Track.Name} is locked.");
        }

        if (found.Clip is not MediaClip { Stream: StreamKind.Video } clip)
        {
            return EditResult.Unchanged(project, label, "Freeze frames apply to video clips only.");
        }

        var media = TimelineQuery.FindMedia(project, clip.MediaId);
        if (media is null)
        {
            return EditResult.Unchanged(project, label, "The clip's media is not in the project.");
        }

        if (media.Info.IsStill)
        {
            return EditResult.Unchanged(project, label, "A still image is already a single frame.");
        }

        if (frame < clip.Start || frame >= clip.End)
        {
            return EditResult.Unchanged(project, label, "The frame to freeze must lie inside the clip.");
        }

        var rate = project.Sequence.Settings.Rate;
        var source = TimeMath.SourceFlicksAt(clip, frame, rate);
        var frozen = new MediaClip(
            "c_freeze",
            0,
            Math.Max(1, durationFrames),
            clip.Enabled,
            null,
            clip.MediaId,
            StreamKind.Video,
            source,
            100,
            clip.Video with { FadeInFrames = 0, FadeOutFrames = 0 },
            AudioProps.Default)
        {
            Freeze = true,
        };

        var start = project;
        var warnings = new List<string>();
        if (frame > clip.Start)
        {
            // The whole link group is split in one call (as SplitLinked does), so the right-hand pieces share one
            // new link id; the insert below then finds nothing left to split on the linked tracks.
            var tracks = project.Sequence.Tracks.Select(t => t.Id).ToList();
            var split = Split(ids, project, tracks, frame, TimelineQuery.LinkGroup(project, clipId));
            warnings.AddRange(split.Warnings);
            start = split.Project;
        }

        var placed = Place(ids, start, [new ClipPlacement(found.Track.Id, frozen)], frame, ripple ? EditMode.Insert : EditMode.Overwrite);
        warnings.AddRange(placed.Warnings);
        if (!placed.Changed(start))
        {
            return EditResult.Unchanged(project, label, [.. warnings.Distinct(StringComparer.Ordinal)]);
        }

        return placed with { Label = label, Warnings = [.. warnings.Distinct(StringComparer.Ordinal)] };
    }
}
