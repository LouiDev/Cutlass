namespace Cutlass.App.ViewModels.Viewer;

/// <summary>Which monitor the viewer shows (DESIGN 4.3).</summary>
public enum ViewerMode
{
    /// <summary>The sequence at the playhead.</summary>
    Timeline,

    /// <summary>A media item opened from the bin.</summary>
    Source,
}
