namespace Cutlass.Media.Settings;

/// <summary>The viewer's preview resolution (DESIGN 7).</summary>
public enum PreviewQuality
{
    /// <summary>The viewer's physical size, capped at 960x540 while playing and 1920x1080 when paused.</summary>
    Auto = 0,

    /// <summary>The sequence resolution.</summary>
    Full = 1,

    /// <summary>Half the sequence resolution.</summary>
    Half = 2,

    /// <summary>A quarter of the sequence resolution.</summary>
    Quarter = 3,
}
