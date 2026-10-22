namespace Cutlass.Media.Jobs;

/// <summary>
/// The kinds of background media job (TECH_BRIEF 6.7). The numeric order is the per-media job order: index, then
/// thumbnails, then waveform, then proxy. Probe and poster run inside the import, before a media item exists.
/// </summary>
public enum JobKind
{
    /// <summary>The packet pass that builds the frame index (6.4).</summary>
    FrameIndex = 0,

    /// <summary>The filmstrip thumbnail sheets (6.5).</summary>
    Thumbnails = 1,

    /// <summary>The waveform peak pyramid (6.5).</summary>
    Waveform = 2,

    /// <summary>The 540-line MJPEG proxy (6.6).</summary>
    Proxy = 3,
}
