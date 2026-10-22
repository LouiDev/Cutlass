namespace Cutlass.Agent.Api.Dto;

/// <summary>One contact sheet cell: the frame it shows and its top-left corner.</summary>
public sealed class ContactCellDto
{
    /// <summary>The timeline frame.</summary>
    public long Frame { get; set; }

    /// <summary>The frame's timecode.</summary>
    public string Timecode { get; set; } = string.Empty;

    /// <summary>Left edge in pixels.</summary>
    public int X { get; set; }

    /// <summary>Top edge in pixels.</summary>
    public int Y { get; set; }
}
