namespace Cutlass.App.ViewModels;

/// <summary>How the media bin sorts its items (DESIGN 4.2).</summary>
public enum MediaSortOrder
{
    /// <summary>The order the media was added to the project.</summary>
    DateAdded,

    /// <summary>By file name.</summary>
    Name,

    /// <summary>By duration, shortest first.</summary>
    Duration,
}
