using Cutlass.Agent.Api;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Agent.Tools;

/// <summary>
/// The edit tools of AGENT_INTEGRATION 4.4, one partial per group (<c>.Clips .Transitions .Titles .Markers .Tracks
/// .Sequence</c>). Each builds a pure edit that checks its ids on the UI thread (unknown ids give <c>not_found</c>)
/// and runs it through <see cref="EditRunner"/>. Edits clamp instead of failing; the clamps come back as warnings.
/// </summary>
internal sealed partial class EditTools(ToolDispatcher d)
{
    private IIdGenerator Ids => d.Host.Services.Ids;

    public void Register()
    {
        RegisterClips();
        RegisterTransitions();
        RegisterTitles();
        RegisterMarkers();
        RegisterTracks();
        RegisterSequence();
    }

    private static Clip RequireClip(Project p, string id) =>
        TimelineQuery.FindClip(p, id)?.Clip ?? throw ApiException.NotFound("clip", id);

    private static void RequireClips(Project p, IEnumerable<string> ids)
    {
        foreach (var id in ids)
        {
            RequireClip(p, id);
        }
    }

    private static Track RequireTrack(Project p, string id) =>
        TimelineQuery.FindTrack(p, id) ?? throw ApiException.NotFound("track", id);

    private static EditMode Mode(string? text) => EnumWire.Parse(text, "mode", EditMode.Overwrite);

    private static IReadOnlyList<string> WithLinked(Project p, IReadOnlyList<string> ids, bool? includeLinked) =>
        includeLinked ?? true ? TimelineQuery.WithLinkedPartners(p, ids) : ids;

    private static EditResult Warn(EditResult result, IEnumerable<string> warnings)
    {
        var extra = warnings.ToList();
        return extra.Count == 0 ? result : result with { Warnings = result.Warnings.AddRange(extra) };
    }
}
