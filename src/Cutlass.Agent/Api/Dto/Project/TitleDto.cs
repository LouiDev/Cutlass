namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>The text and style of a title (<c>TitleSpec</c>). In requests every field is optional (null keeps the value).</summary>
public sealed class TitleDto
{
    /// <summary>The text; lines separated by <c>\n</c>.</summary>
    public string? Text { get; set; }

    /// <summary><c>builtin:Inter-Bold</c>, a system family or a font file.</summary>
    public string? Font { get; set; }

    /// <summary>Font size in canvas pixels.</summary>
    public double? Size { get; set; }

    /// <summary>Text colour, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    public string? Color { get; set; }

    /// <summary>Outline width in pixels.</summary>
    public double? OutlineWidth { get; set; }

    /// <summary>Outline colour.</summary>
    public string? OutlineColor { get; set; }

    /// <summary>Draw a background box.</summary>
    public bool? Box { get; set; }

    /// <summary>Box colour.</summary>
    public string? BoxColor { get; set; }

    /// <summary>Box padding in pixels.</summary>
    public double? BoxPadding { get; set; }

    /// <summary><c>center</c>, <c>lowerThird</c> or <c>top</c>.</summary>
    public string? Anchor { get; set; }

    /// <summary>Offset from the anchor in pixels.</summary>
    public double? OffsetX { get; set; }

    /// <summary>Offset from the anchor in pixels.</summary>
    public double? OffsetY { get; set; }
}
