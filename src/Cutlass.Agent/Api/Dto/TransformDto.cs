namespace Cutlass.Agent.Api.Dto;

/// <summary>A clip transform. In requests every field is optional (null keeps the value).</summary>
public sealed class TransformDto
{
    /// <summary>Horizontal offset from the canvas centre in pixels.</summary>
    public double? X { get; set; }

    /// <summary>Vertical offset from the canvas centre in pixels.</summary>
    public double? Y { get; set; }

    /// <summary>Scale (1 fits the canvas).</summary>
    public double? Scale { get; set; }

    /// <summary>Left crop, 0 to 0.9.</summary>
    public double? CropLeft { get; set; }

    /// <summary>Top crop, 0 to 0.9.</summary>
    public double? CropTop { get; set; }

    /// <summary>Right crop, 0 to 0.9.</summary>
    public double? CropRight { get; set; }

    /// <summary>Bottom crop, 0 to 0.9.</summary>
    public double? CropBottom { get; set; }
}
