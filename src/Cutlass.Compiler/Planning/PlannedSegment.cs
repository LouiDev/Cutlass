using Cutlass.Core.Model;

namespace Cutlass.Compiler.Planning;

/// <summary>
/// The part <c>[Start, End)</c> of one clip that a render needs, with the clip's extent and the transitions at its
/// ends (TECH_BRIEF 3.2 and 7.3). Only transitions inside the clip's run count: a transition towards a disabled clip
/// is not rendered.
/// </summary>
/// <param name="Clip">The clip.</param>
/// <param name="ExtentStart">The first frame of the clip's extent, <c>Start - headExt</c>.</param>
/// <param name="ExtentEnd">The end of the clip's extent, <c>End + tailExt</c>.</param>
/// <param name="Start">The first timeline frame rendered (<c>g0</c>).</param>
/// <param name="End">The end of the rendered frames (<c>g1</c>, exclusive).</param>
/// <param name="Head">The transition on the clip's start cut, or null.</param>
/// <param name="Tail">The transition on the clip's end cut, or null.</param>
public sealed record PlannedSegment(
    Clip Clip,
    long ExtentStart,
    long ExtentEnd,
    long Start,
    long End,
    Transition? Head,
    Transition? Tail)
{
    /// <summary>The rendered frame count, <c>n = g1 - g0</c>.</summary>
    public long Length => End - Start;

    /// <summary>The segment offset into the extent, <c>o = g0 - ExtentStart</c>.</summary>
    public long Offset => Start - ExtentStart;

    /// <summary>The extent length <c>L</c> in frames.</summary>
    public long ExtentLength => ExtentEnd - ExtentStart;
}
