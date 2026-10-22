namespace Cutlass.Media.Projects;

/// <summary>How a project's media file was found on open (DESIGN 11).</summary>
public enum MediaLinkState
{
    /// <summary>The file exists with the size and modification time recorded in the project.</summary>
    Online,

    /// <summary>The file exists but its size or modification time changed: it is re-probed ("changed" badge).</summary>
    Changed,

    /// <summary>The file was found neither by its absolute path nor relative to the project.</summary>
    Offline,
}
