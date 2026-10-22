namespace Cutlass.Media.Platform;

/// <summary>What a path names (see <see cref="FileIdentity.KindOf"/>).</summary>
public enum FileKind
{
    /// <summary>Nothing, or nothing readable.</summary>
    Missing,

    /// <summary>A regular file: the only kind media, fonts and projects are read from.</summary>
    Regular,

    /// <summary>A folder.</summary>
    Directory,

    /// <summary>A device, FIFO or socket, which may block or never end when read.</summary>
    Other,
}
