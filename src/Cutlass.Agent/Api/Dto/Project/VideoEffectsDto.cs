namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>Static video effects of a video clip. In requests every field is optional (null keeps the value).</summary>
public sealed class VideoEffectsDto
{
    /// <summary>Mirror horizontally (false).</summary>
    public bool? FlipH { get; set; }

    /// <summary>Mirror vertically (false).</summary>
    public bool? FlipV { get; set; }

    /// <summary>Rotation in degrees, -180 to 180 (0, clockwise positive).</summary>
    public double? RotationDegrees { get; set; }

    /// <summary>Hue shift in degrees, -180 to 180 (0).</summary>
    public double? HueDegrees { get; set; }

    /// <summary>Colour temperature in kelvin, 1000 to 40000 (6500).</summary>
    public double? TemperatureKelvin { get; set; }

    /// <summary>Green to magenta tint, -1 to 1 (0).</summary>
    public double? Tint { get; set; }

    /// <summary>Gaussian blur in sequence pixels, 0 to 50 (0).</summary>
    public double? Blur { get; set; }

    /// <summary>Sharpen amount, 0 to 1 (0).</summary>
    public double? Sharpen { get; set; }

    /// <summary>Vignette strength, 0 to 1 (0).</summary>
    public double? Vignette { get; set; }

    /// <summary>Drop the colour (false).</summary>
    public bool? Grayscale { get; set; }

    /// <summary>Chroma key on (false).</summary>
    public bool? KeyEnabled { get; set; }

    /// <summary>Key colour "#RRGGBB" ("#00FF00").</summary>
    public string? KeyColor { get; set; }

    /// <summary>Key similarity, 0.01 to 1 (0.1).</summary>
    public double? KeySimilarity { get; set; }

    /// <summary>Key edge blend, 0 to 1 (0).</summary>
    public double? KeyBlend { get; set; }
}
