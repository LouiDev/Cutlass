namespace Cutlass.Agent.Api.Dto;

/// <summary>The result of <c>POST /v1/media/import</c>.</summary>
public sealed class ImportResultDto
{
    /// <summary>The imported media.</summary>
    public List<MediaDto> Imported { get; set; } = [];

    /// <summary>The files that failed.</summary>
    public List<ImportFailureDto> Failed { get; set; } = [];
}
