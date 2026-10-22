namespace Cutlass.Agent.Api.Dto;

/// <summary>A volume key: a frame relative to the clip start and a level in dB.</summary>
public sealed class VolumeKeyDto
{
    /// <summary>Frame from the clip start.</summary>
    public long Frame { get; set; }

    /// <summary>Level in dB.</summary>
    public double Db { get; set; }
}
