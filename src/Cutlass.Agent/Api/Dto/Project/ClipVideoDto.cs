namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>The video properties of a video media clip.</summary>
public sealed class ClipVideoDto
{
    /// <summary>Position, scale and crop.</summary>
    public TransformDto Transform { get; set; } = new();

    /// <summary>Opacity 0 to 1.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Colour adjustment.</summary>
    public ColorDto Color { get; set; } = new();

    /// <summary>Fade-in frames.</summary>
    public long FadeInFrames { get; set; }

    /// <summary>Fade-out frames.</summary>
    public long FadeOutFrames { get; set; }
}
