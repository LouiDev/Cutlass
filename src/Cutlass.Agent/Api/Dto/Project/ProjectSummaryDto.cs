using Cutlass.Agent.Api.Dto.Media;

namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>The project summary of AGENT_INTEGRATION 4.5.</summary>
public sealed class ProjectSummaryDto
{
    /// <summary>The revision.</summary>
    public long Revision { get; set; }

    /// <summary>The project file (forward slashes), or null while untitled.</summary>
    public string? Path { get; set; }

    /// <summary>The project name.</summary>
    public string Name { get; set; } = "Untitled";

    /// <summary>True with unsaved changes.</summary>
    public bool Dirty { get; set; }

    /// <summary>The sequence.</summary>
    public SequenceSummaryDto Sequence { get; set; } = new();

    /// <summary>The tracks, top to bottom.</summary>
    public List<TrackSummaryDto> Tracks { get; set; } = [];

    /// <summary>The markers by frame.</summary>
    public List<MarkerSummaryDto> Markers { get; set; } = [];

    /// <summary>The media.</summary>
    public List<MediaDto> Media { get; set; } = [];
}
