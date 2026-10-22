namespace Cutlass.App.Controls;

/// <summary>A selected gap: empty space between clips of one track (DESIGN 5.3).</summary>
/// <param name="TrackId">The track.</param>
/// <param name="Start">The first frame of the gap.</param>
/// <param name="End">The end of the gap (exclusive).</param>
public sealed record TimelineGap(string TrackId, long Start, long End);
