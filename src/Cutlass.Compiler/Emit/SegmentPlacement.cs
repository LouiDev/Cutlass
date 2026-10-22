namespace Cutlass.Compiler.Emit;

/// <summary>How a video segment reaches canvas size (TECH_BRIEF 7.4, canvas step).</summary>
internal enum SegmentPlacement
{
    /// <summary>The segment is already canvas sized (titles, offline stand-ins, an exact fit).</summary>
    CanvasSized = 0,

    /// <summary>Identity transform with a smaller fit: centred with <c>pad</c> in multi-segment runs.</summary>
    IdentityFit = 1,

    /// <summary>A user transform: placed on a transparent canvas with <c>overlay</c> in multi-segment runs.</summary>
    Transformed = 2,
}
