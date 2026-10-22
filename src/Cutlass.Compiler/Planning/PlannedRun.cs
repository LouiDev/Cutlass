using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Compiler.Planning;

/// <summary>
/// The part of one video run (a maximal butting sequence of enabled clips) that a window needs (TECH_BRIEF 7.4 and
/// 7.5). The run renders <c>[RenderStart, RenderEnd)</c>, which is the visible interval widened outward over any
/// transition region an end falls inside; <c>trim</c> cuts it back to <c>[VisibleStart, VisibleEnd)</c> after the
/// joins.
/// </summary>
/// <param name="Segments">The segments in timeline order. Consecutive segments butt (cut) or overlap by a transition.</param>
/// <param name="Joins">For each pair of consecutive segments, the transition joining them, or null for a plain cut.
/// Its length is <c>Segments.Length - 1</c>.</param>
/// <param name="RenderStart">The first frame the joined run produces.</param>
/// <param name="RenderEnd">The end of the frames the joined run produces.</param>
/// <param name="VisibleStart">The first frame placed on the canvas, <c>max(run start, window start)</c>.</param>
/// <param name="VisibleEnd">The end of the placed frames, <c>min(run end, window end)</c>.</param>
/// <param name="RunClipCount">The number of clips in the whole run, whatever the window needs. The pixel path of a
/// frame must not depend on the window, so a run of several clips always takes the canvas step.</param>
/// <param name="RunHasTransition">True when the whole run has at least one transition (its frames pass through
/// <c>xfade</c> in a full render).</param>
public sealed record PlannedRun(
    ImmutableArray<PlannedSegment> Segments,
    ImmutableArray<Transition?> Joins,
    long RenderStart,
    long RenderEnd,
    long VisibleStart,
    long VisibleEnd,
    int RunClipCount,
    bool RunHasTransition)
{
    /// <summary>True when the run was widened and needs <c>trim</c> after the joins.</summary>
    public bool IsWidened => RenderStart != VisibleStart || RenderEnd != VisibleEnd;

    /// <summary>The trim start frame relative to the joined stream (<c>A</c>).</summary>
    public long TrimStart => VisibleStart - RenderStart;

    /// <summary>The trim end frame relative to the joined stream (<c>B</c>).</summary>
    public long TrimEnd => VisibleEnd - RenderStart;

    /// <summary>True when this window joins at least one pair of segments with a transition.</summary>
    public bool HasXfade => Joins.Any(j => j is not null);
}
