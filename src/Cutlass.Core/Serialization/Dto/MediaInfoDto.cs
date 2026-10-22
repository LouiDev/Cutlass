namespace Cutlass.Core.Serialization.Dto;

/// <summary>Probe results in the project file.</summary>
internal sealed class MediaInfoDto
{
    public long DurationUs { get; set; }

    public long FormatStartUs { get; set; }

    public string FormatName { get; set; } = string.Empty;

    public bool Still { get; set; }

    public VideoStreamInfoDto? Video { get; set; }

    public AudioStreamInfoDto? Audio { get; set; }
}
