namespace Cutlass.Agent.Api.Dto.Media;

/// <summary>One file an import could not add.</summary>
public sealed class ImportFailureDto
{
    /// <summary>The file.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Why.</summary>
    public string Message { get; set; } = string.Empty;
}
