namespace Cutlass.Core.Model;

/// <summary>Which stream of a media asset a <see cref="MediaClip"/> uses.</summary>
public enum StreamKind
{
    /// <summary>The video stream (or the still image).</summary>
    Video,

    /// <summary>The audio stream.</summary>
    Audio,
}
