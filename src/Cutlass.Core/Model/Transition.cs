namespace Cutlass.Core.Model;

/// <summary>
/// A transition on the cut between two butting clips of one track. With <c>a = Duration / 2</c> (floor) and
/// <c>b = Duration - a</c> it covers <c>[cut - a, cut + b)</c>.
/// </summary>
/// <param name="Id">Transition id (prefix <c>x_</c>).</param>
/// <param name="Kind">The transition effect.</param>
/// <param name="LeftClipId">The outgoing clip, which ends at the cut.</param>
/// <param name="RightClipId">The incoming clip, which starts at the cut.</param>
/// <param name="Duration">Length in frames, at least 2.</param>
public sealed record Transition(string Id, TransitionKind Kind, string LeftClipId, string RightClipId, long Duration);
