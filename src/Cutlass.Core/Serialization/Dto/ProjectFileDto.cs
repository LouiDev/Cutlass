namespace Cutlass.Core.Serialization.Dto;

/// <summary>The root of a <c>.cutlass</c> file (TECH_BRIEF 3.5).</summary>
internal sealed class ProjectFileDto
{
    public string Format { get; set; } = ProjectSerializer.FormatName;

    public int SchemaVersion { get; set; }

    public string App { get; set; } = string.Empty;

    public List<MediaAssetDto> Media { get; set; } = [];

    public SequenceDto Sequence { get; set; } = new();
}
