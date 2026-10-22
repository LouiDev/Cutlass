namespace Cutlass.App.Services;

/// <summary>
/// Data formats used for drag and drop inside the app. The media bin and the Source monitor publish these and the
/// timeline accepts them (effects use <see cref="ViewModels.EffectItem.DragFormat"/>).
/// </summary>
public static class DragFormats
{
    /// <summary>A single media asset id (string) dragged from the media bin onto the timeline.</summary>
    public const string MediaAssetId = "cutlass/media-asset-id";

    /// <summary>Several media asset ids joined with '\n', dragged from the media bin onto the timeline.</summary>
    public const string MediaAssetIds = "cutlass/media-asset-ids";

    /// <summary>
    /// A Source monitor range (<see cref="SourceDrag.Format"/>): the canvas drags video and audio, the "Video only" and
    /// "Audio only" handles one stream (DESIGN 4.3).
    /// </summary>
    public const string SourceRange = "cutlass/source-range";
}
