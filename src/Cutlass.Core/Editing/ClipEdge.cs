namespace Cutlass.Core.Editing;

/// <summary>Which end of a clip a trim acts on.</summary>
public enum ClipEdge
{
    /// <summary>The first frame (the in point).</summary>
    Start,

    /// <summary>The frame after the last one (the out point).</summary>
    End,
}
