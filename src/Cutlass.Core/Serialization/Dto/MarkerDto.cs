namespace Cutlass.Core.Serialization.Dto;

/// <summary>A marker in the project file.</summary>
internal sealed class MarkerDto
{
    public string Id { get; set; } = string.Empty;

    public long Frame { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#F5D547";
}
