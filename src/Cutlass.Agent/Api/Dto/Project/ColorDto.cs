namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>A colour adjustment. In requests every field is optional (null keeps the value).</summary>
public sealed class ColorDto
{
    /// <summary>Brightness, -1 to 1 (neutral 0).</summary>
    public double? Brightness { get; set; }

    /// <summary>Contrast, 0 to 2 (neutral 1).</summary>
    public double? Contrast { get; set; }

    /// <summary>Saturation, 0 to 3 (neutral 1).</summary>
    public double? Saturation { get; set; }

    /// <summary>Gamma, 0.1 to 4 (neutral 1).</summary>
    public double? Gamma { get; set; }
}
