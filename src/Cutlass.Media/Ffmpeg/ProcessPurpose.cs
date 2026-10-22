namespace Cutlass.Media.Ffmpeg;

/// <summary>Why a child process runs. It decides stdin handling, priority and how cancellation stops it.</summary>
public enum ProcessPurpose
{
    /// <summary>Short queries: <c>ffprobe</c>, <c>-version</c>, <c>-filters</c>, <c>-encoders</c>, the script probe.</summary>
    Probe = 0,

    /// <summary>Scrub frames and playback readers piping rawvideo or PCM to stdout.</summary>
    Preview = 1,

    /// <summary>Cache jobs: frame index, poster, thumbnails, waveform, proxies. Run at below-normal priority.</summary>
    Background = 2,

    /// <summary>Exports. The only purpose without <c>-nostdin</c>: <c>q</c> on stdin is the graceful stop.</summary>
    Export = 3,
}
