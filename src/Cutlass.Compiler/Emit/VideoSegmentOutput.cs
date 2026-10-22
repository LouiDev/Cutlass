using Cutlass.Compiler.Graph;

namespace Cutlass.Compiler.Emit;

/// <summary>An emitted video segment chain (still open) and where it goes on the canvas.</summary>
/// <param name="Chain">The open chain, ending in <c>yuva420p</c> at the segment's scaled size.</param>
/// <param name="Placement">How the segment reaches canvas size.</param>
/// <param name="X">The overlay x position on the canvas.</param>
/// <param name="Y">The overlay y position on the canvas.</param>
/// <param name="Width">The scaled width <c>SW</c>.</param>
/// <param name="Height">The scaled height <c>SH</c>.</param>
internal sealed record VideoSegmentOutput(GraphChain Chain, SegmentPlacement Placement, long X, long Y, long Width, long Height);
