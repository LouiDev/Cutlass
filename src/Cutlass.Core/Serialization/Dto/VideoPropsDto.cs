namespace Cutlass.Core.Serialization.Dto;

/// <summary>Video properties of a media clip in the project file.</summary>
internal sealed class VideoPropsDto
{
    public TransformDto Transform { get; set; } = new();

    public double Opacity { get; set; } = 1;

    public ColorAdjustDto Color { get; set; } = new();

    public long FadeIn { get; set; }

    public long FadeOut { get; set; }
}
