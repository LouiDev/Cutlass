namespace Cutlass.App.Controls.Drag;

/// <summary>What the viewer shows during a drag (TECH_BRIEF 10.2): the edge frame of a trim, or two-up frames.</summary>
/// <param name="Frame">The timeline frame to show.</param>
/// <param name="SecondFrame">The second frame of a two-up view (roll), or null.</param>
public sealed record ViewerHint(long Frame, long? SecondFrame = null);
