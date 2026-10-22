namespace Cutlass.Core.Serialization.Dto;

/// <summary>Title text and style in the project file.</summary>
internal sealed class TitleSpecDto
{
    public string Text { get; set; } = string.Empty;

    public string Font { get; set; } = "builtin:Inter-Bold";

    public double Size { get; set; } = 96;

    public string Color { get; set; } = "#FFFFFFFF";

    public double OutlineWidth { get; set; }

    public string OutlineColor { get; set; } = "#00000099";

    public bool Box { get; set; }

    public string BoxColor { get; set; } = "#00000080";

    public double BoxPadding { get; set; }

    public string Anchor { get; set; } = "center";

    public double OffsetX { get; set; }

    public double OffsetY { get; set; }
}
