namespace Cutlass.Core.Editing;

/// <summary>How placed or moved clips treat the material already on the target tracks (DESIGN 5.5).</summary>
public enum EditMode
{
    /// <summary>Covered clips are trimmed, split or removed. The default drop.</summary>
    Overwrite,

    /// <summary>Later clips on every unlocked track move right to make room.</summary>
    Insert,
}
