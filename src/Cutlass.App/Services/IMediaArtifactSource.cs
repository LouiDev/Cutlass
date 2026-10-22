using Cutlass.Core.Model;
using Cutlass.Media.Services;

namespace Cutlass.App.Services;

/// <summary>
/// Thumbnail sheets and peak pyramids that already exist, for drawing (filmstrips, waveforms, bin skim, viewer
/// placeholder). Lookups are memory only, so they are safe on the UI thread; <see cref="Changed"/> says when a new
/// artifact arrived.
/// </summary>
public interface IMediaArtifactSource
{
    /// <summary>Raised on the UI thread with the media id when one of its artifacts became available.</summary>
    event EventHandler<string>? Changed;

    /// <summary>The thumbnail sheets of a media item, or null while they do not exist.</summary>
    /// <param name="asset">The media.</param>
    /// <returns>The sheets, or null.</returns>
    ThumbnailSheet? TryGetSheet(MediaAsset asset);

    /// <summary>The waveform peaks of a media item, or null while they do not exist.</summary>
    /// <param name="asset">The media.</param>
    /// <returns>The pyramid, or null.</returns>
    PeakPyramid? TryGetPeaks(MediaAsset asset);
}
