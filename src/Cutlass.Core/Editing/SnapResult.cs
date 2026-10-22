namespace Cutlass.Core.Editing;

/// <summary>The outcome of <see cref="SnapEngine.Resolve"/>.</summary>
/// <param name="Delta">The delta to apply: the snapped delta when a snap engaged, else the requested one.</param>
/// <param name="SnapFrame">The target frame that engaged (draw the snap line there), or null when nothing snapped.</param>
public sealed record SnapResult(long Delta, long? SnapFrame)
{
    /// <summary>True when a snap engaged.</summary>
    public bool Snapped => SnapFrame.HasValue;
}
