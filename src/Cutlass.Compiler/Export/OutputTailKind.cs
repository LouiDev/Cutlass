namespace Cutlass.Compiler.Export;

/// <summary>Which video tail ends the graph (TECH_BRIEF 7.7).</summary>
public enum OutputTailKind
{
    /// <summary>BT.709 tags, then <c>format=</c> the preset's pixel format.</summary>
    Export = 0,

    /// <summary>Optional <c>framestep</c>, BT.709 tags, then <c>format=bgra</c> for rawvideo previews.</summary>
    Preview = 1,

    /// <summary>BT.709 tags, <c>fps=15</c> and the two-pass palette chain of an animated GIF.</summary>
    Gif = 2,
}
