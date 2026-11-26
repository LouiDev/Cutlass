namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>One marker of the project summary.</summary>
public sealed class MarkerSummaryDto
{
    /// <summary>The id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The frame.</summary>
    public long Frame { get; set; }

    /// <summary>The label.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The colour, <c>#RRGGBB</c>.</summary>
    public string Color { get; set; } = "#F5D547";
}
