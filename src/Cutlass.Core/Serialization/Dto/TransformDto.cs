namespace Cutlass.Core.Serialization.Dto;

/// <summary>A transform in the project file; <c>crop</c> is <c>[left, top, right, bottom]</c>.</summary>
internal sealed class TransformDto
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Scale { get; set; } = 1;

    public double[] Crop { get; set; } = [0, 0, 0, 0];
}
