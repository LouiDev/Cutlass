using System.Collections.Immutable;
using Cutlass.App.Services;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.App.ViewModels.Timeline;

/// <summary>
/// Drops onto the timeline besides media bin items (DESIGN 4.2, 4.3): Source monitor ranges with a stream choice and
/// Effects tab items. The <see cref="Controls.TimelineControl"/> reads the drag data and calls these.
/// </summary>
public sealed partial class TimelineViewModel
{
    /// <summary>
    /// The Effects tab, which handles effect drops (transitions on a cut, titles on a video track, fades on a clip).
    /// Set by the shell; while it is null the timeline refuses effect drops.
    /// </summary>
    public EffectsViewModel? Effects { get; set; }

    /// <summary>
    /// The clips a Source monitor drop would place, with starts relative to the drop frame (the drag ghost): the Source
    /// range on the hovered track and, for <see cref="SourceStreams.Both"/>, its linked partner on the matching track of
    /// the other kind (same index, else V1 or A1). "Video only" and "Audio only" place one unlinked stream; when the
    /// hovered track is of the other kind, the matching track of the dragged kind takes it.
    /// </summary>
    /// <param name="drag">The Source range.</param>
    /// <param name="trackId">The hovered track.</param>
    /// <returns>The placements (empty when the media lacks the stream or no track can take it).</returns>
    public ImmutableArray<ClipPlacement> PreviewSourcePlacements(SourceDrag drag, string trackId)
    {
        ArgumentNullException.ThrowIfNull(drag);
        ArgumentNullException.ThrowIfNull(trackId);
        return SourceDropPlacements(Session.Current, drag, trackId);
    }

    /// <summary>
    /// A Source monitor drop (DESIGN 4.3): places the Source range at <paramref name="frame"/> as one undo step and
    /// selects the new clips. The playhead stays put, as for media bin drops.
    /// </summary>
    /// <param name="drag">The Source range.</param>
    /// <param name="trackId">The hovered track.</param>
    /// <param name="frame">The drop frame.</param>
    /// <param name="insert">True for insert (Ctrl at drop), false for overwrite.</param>
    public void PlaceSource(SourceDrag drag, string trackId, long frame, bool insert)
    {
        ArgumentNullException.ThrowIfNull(drag);
        ArgumentNullException.ThrowIfNull(trackId);
        var mode = insert ? EditMode.Insert : EditMode.Overwrite;
        var result = Run(p =>
        {
            var placements = SourceDropPlacements(p, drag, trackId);
            return placements.IsEmpty
                ? EditResult.Unchanged(p, mode == EditMode.Insert ? "Insert" : "Overwrite", "The media has no stream for this track.")
                : TimelineEdits.Place(Ids, p, placements, Math.Max(0, frame), mode);
        });
        if (!result.Select.IsDefaultOrEmpty)
        {
            Select(result.Select, false);
        }
    }

    private ImmutableArray<ClipPlacement> SourceDropPlacements(Project project, SourceDrag drag, string trackId)
    {
        if (DropTargets(project, trackId) is not { } targets)
        {
            return ImmutableArray<ClipPlacement>.Empty;
        }

        var (video, audio) = targets;

        var duration = TimelineEdits.SourceRangeFrames(project, drag.AssetId, drag.InFlicks, drag.OutFlicks, StillDurationFrames);
        if (duration < 1)
        {
            return ImmutableArray<ClipPlacement>.Empty;
        }

        return TimelineEdits.SourcePlacements(
            project,
            drag.AssetId,
            drag.InFlicks,
            duration,
            drag.Streams == SourceStreams.Audio ? null : video,
            drag.Streams == SourceStreams.Video ? null : audio);
    }
}
