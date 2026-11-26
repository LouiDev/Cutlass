namespace Cutlass.App.Services.Playback;

/// <summary>A range to play (see <see cref="PlaybackPlanner"/>).</summary>
/// <param name="Start">The first frame.</param>
/// <param name="End">The end frame (exclusive).</param>
/// <param name="Loop">True to repeat.</param>
/// <param name="LoopStart">Where a repeat starts, or null.</param>
public readonly record struct PlayRange(long Start, long End, bool Loop, long? LoopStart);
