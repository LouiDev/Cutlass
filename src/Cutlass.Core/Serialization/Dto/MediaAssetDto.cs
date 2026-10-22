namespace Cutlass.Core.Serialization.Dto;

/// <summary>A media asset in the project file.</summary>
internal sealed class MediaAssetDto
{
    public string Id { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string? RelativePath { get; set; }

    public string Fingerprint { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string MtimeUtc { get; set; } = string.Empty;

    public MediaInfoDto Info { get; set; } = new();
}
