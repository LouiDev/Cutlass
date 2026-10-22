namespace Cutlass.Core.Serialization.Dto;

/// <summary>Colour adjustments in the project file (neutral 0, 1, 1, 1).</summary>
internal sealed class ColorAdjustDto
{
    public double Brightness { get; set; }

    public double Contrast { get; set; } = 1;

    public double Saturation { get; set; } = 1;

    public double Gamma { get; set; } = 1;
}
