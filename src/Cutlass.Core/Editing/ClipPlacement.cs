using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>
/// One clip to place with <see cref="TimelineEdits.Place"/>. The clip's <see cref="Model.Clip.Start"/> is an offset in
/// frames relative to the placement frame, so a group keeps its relative layout. Ids are placeholders: placing assigns
/// fresh clip ids, and clips sharing a placeholder <see cref="Model.Clip.LinkId"/> get one fresh link group.
/// </summary>
/// <param name="TrackId">The target track.</param>
/// <param name="Clip">The clip, with <c>Start</c> relative to the placement frame.</param>
public sealed record ClipPlacement(string TrackId, Clip Clip);
