namespace Cutlass.Media.Cache;

/// <summary>
/// The artifacts a media cache folder can hold (TECH_BRIEF 6.1), with their file names and current versions. Bumping
/// a version invalidates only that artifact: the manifest records the version each artifact was written with, and a
/// different number counts as missing.
/// </summary>
public static class CacheArtifacts
{
    /// <summary>The raw ffprobe output, <c>probe.json</c>.</summary>
    public const string Probe = "probe";

    /// <summary>The frame index, <c>frames.idx</c>.</summary>
    public const string Index = "index";

    /// <summary>The media bin poster, <c>poster.jpg</c>.</summary>
    public const string Poster = "poster";

    /// <summary>The thumbnail sheets, the <c>thumbs/</c> folder.</summary>
    public const string Thumbnails = "thumbs";

    /// <summary>The waveform peak pyramid, <c>peaks.bin</c>.</summary>
    public const string Peaks = "peaks";

    /// <summary>The 540-line proxy, <c>proxy540.mov</c> plus <c>proxy540.idx</c>.</summary>
    public const string Proxy = "proxy540";

    /// <summary>The file name of the frame index.</summary>
    public const string IndexFile = "frames.idx";

    /// <summary>The file name of the raw probe output.</summary>
    public const string ProbeFile = "probe.json";

    /// <summary>The file name of the poster.</summary>
    public const string PosterFile = "poster.jpg";

    /// <summary>The folder name of the thumbnail sheets.</summary>
    public const string ThumbnailsFolder = "thumbs";

    /// <summary>The file name of the thumbnail geometry inside <see cref="ThumbnailsFolder"/>.</summary>
    public const string ThumbnailsIndexFile = "thumbs.json";

    /// <summary>The file name of the peak pyramid.</summary>
    public const string PeaksFile = "peaks.bin";

    /// <summary>The file name of the proxy movie.</summary>
    public const string ProxyFile = "proxy540.mov";

    /// <summary>The file name of the proxy frame index (frames.idx format).</summary>
    public const string ProxyIndexFile = "proxy540.idx";

    /// <summary>The current version of every artifact.</summary>
    public static IReadOnlyDictionary<string, int> CurrentVersions { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [Probe] = 1,
        [Index] = 2, // 2: no dts-fallback index for B-frame AVI (TECH_BRIEF 6.4)
        [Poster] = 1,
        [Thumbnails] = 1,
        [Peaks] = 2, // 2: peaks start at format.start_time (late audio padded)
        [Proxy] = 1,
    };

    /// <summary>The current version of an artifact.</summary>
    /// <param name="artifact">The artifact name.</param>
    /// <returns>The version.</returns>
    /// <exception cref="ArgumentException">When the artifact is unknown.</exception>
    public static int VersionOf(string artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return CurrentVersions.TryGetValue(artifact, out var version)
            ? version
            : throw new ArgumentException($"'{artifact}' is not a cache artifact.", nameof(artifact));
    }
}
