namespace Cutlass.Core.Serialization.Dto;

/// <summary>Static video effects in the project file. Every initializer is the neutral value, so a missing field reads as "no effect".</summary>
internal sealed class VideoEffectsDto
{
    public bool FlipH { get; set; }

    public bool FlipV { get; set; }

    public double RotationDegrees { get; set; }

    public double HueDegrees { get; set; }

    public double TemperatureKelvin { get; set; } = 6500;

    public double Tint { get; set; }

    public double Blur { get; set; }

    public double Sharpen { get; set; }

    public double Vignette { get; set; }

    public bool Grayscale { get; set; }

    public bool KeyEnabled { get; set; }

    public string KeyColor { get; set; } = "#00FF00";

    public double KeySimilarity { get; set; } = 0.1;

    public double KeyBlend { get; set; }
}
