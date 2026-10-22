using Cutlass.Core.Model;
using Cutlass.Media.Jobs;
using Cutlass.Media.Services;

namespace Cutlass.App.Services;

/// <summary>
/// The <see cref="IMediaArtifactSource"/> over the thumbnail and waveform services. The scheduler's
/// <see cref="JobScheduler.ArtifactReady"/> (posted to the UI thread) raises <see cref="Changed"/>.
/// </summary>
public sealed class MediaArtifactSource : IMediaArtifactSource
{
    private readonly ThumbnailService _thumbnails;
    private readonly WaveformService _waveforms;

    /// <summary>Creates the source and listens to the scheduler.</summary>
    /// <param name="thumbnails">The thumbnail service.</param>
    /// <param name="waveforms">The waveform service.</param>
    /// <param name="scheduler">The job scheduler (created with the UI synchronization context).</param>
    public MediaArtifactSource(ThumbnailService thumbnails, WaveformService waveforms, JobScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(thumbnails);
        ArgumentNullException.ThrowIfNull(waveforms);
        ArgumentNullException.ThrowIfNull(scheduler);
        _thumbnails = thumbnails;
        _waveforms = waveforms;
        scheduler.ArtifactReady += (mediaId, kind) =>
        {
            if (kind is JobKind.Thumbnails or JobKind.Waveform)
            {
                Changed?.Invoke(this, mediaId);
            }
        };
    }

    /// <inheritdoc />
    public event EventHandler<string>? Changed;

    /// <inheritdoc />
    public ThumbnailSheet? TryGetSheet(MediaAsset asset) => _thumbnails.TryGet(asset);

    /// <inheritdoc />
    public PeakPyramid? TryGetPeaks(MediaAsset asset) => _waveforms.TryGet(asset);
}
