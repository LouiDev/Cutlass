namespace Cutlass.App.ViewModels;

/// <summary>
/// Arguments of <see cref="TimelineViewModel.MatchFrameRequested"/> (F, DESIGN 5.5): the media of the clip under the
/// playhead and the source time of the playhead frame inside it.
/// </summary>
public sealed class MatchFrameEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="assetId">The media id.</param>
    /// <param name="sourceFlicks">The source time in flicks (from <c>format.start_time</c>).</param>
    public MatchFrameEventArgs(string assetId, long sourceFlicks)
    {
        ArgumentNullException.ThrowIfNull(assetId);
        AssetId = assetId;
        SourceFlicks = sourceFlicks;
    }

    /// <summary>The media id to open in Source mode.</summary>
    public string AssetId { get; }

    /// <summary>The matching source time in flicks.</summary>
    public long SourceFlicks { get; }
}
