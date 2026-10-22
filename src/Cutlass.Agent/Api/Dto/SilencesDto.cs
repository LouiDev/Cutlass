namespace Cutlass.Agent.Api.Dto;

/// <summary>The result of <c>POST /v1/audio/silences</c>.</summary>
public sealed class SilencesDto
{
    /// <summary>The silent ranges in frames.</summary>
    public List<SilenceRangeDto> Ranges { get; set; } = [];
}
