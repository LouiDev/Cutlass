using Cutlass.Core.Model;

namespace Cutlass.Media.Projects;

/// <summary>
/// The result of checking every media file of a project on open (DESIGN 11): the project with the paths of moved
/// files updated (found relative to the project), and the link state of every asset.
/// </summary>
/// <param name="Project">The project, the same reference when no path changed.</param>
/// <param name="States">The link state of every media id.</param>
/// <param name="MovedIds">The media found relative to the project instead of at their absolute path.</param>
public sealed record RelinkScan(Project Project, IReadOnlyDictionary<string, MediaLinkState> States, IReadOnlyList<string> MovedIds)
{
    /// <summary>
    /// The media whose only paths are on a remote volume other than the project's (<see cref="MediaLocation"/>). They
    /// were not touched and count as offline until the user relinks them or confirms the path; confirming means
    /// scanning again with their ids (<see cref="MediaRelinker.ScanAsync(Core.Model.Project, string?, IReadOnlyCollection{string}, CancellationToken)"/>).
    /// </summary>
    public IReadOnlyList<string> UnconfirmedIds { get; init; } = [];

    /// <summary>The ids of offline media, in project order.</summary>
    public IReadOnlyList<string> OfflineIds => [.. Project.Media.Where(m => State(m.Id) == MediaLinkState.Offline).Select(m => m.Id)];

    /// <summary>The ids of changed media, in project order.</summary>
    public IReadOnlyList<string> ChangedIds => [.. Project.Media.Where(m => State(m.Id) == MediaLinkState.Changed).Select(m => m.Id)];

    /// <summary>The state of one media id (unknown ids count as online).</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>The state.</returns>
    public MediaLinkState State(string mediaId) => States.TryGetValue(mediaId, out var state) ? state : MediaLinkState.Online;
}
