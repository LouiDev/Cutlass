namespace Cutlass.Core.Serialization.Dto;

/// <summary>A volume keyframe in the project file.</summary>
internal sealed class VolumeKeyDto
{
    public long Frame { get; set; }

    public double Db { get; set; }
}
