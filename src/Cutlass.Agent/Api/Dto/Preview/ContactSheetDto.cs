using System.Text.Json.Serialization;

namespace Cutlass.Agent.Api.Dto.Preview;

/// <summary>The layout of a contact sheet; with <c>format=base64</c> also the PNG.</summary>
public sealed class ContactSheetDto
{
    /// <summary>Sheet width.</summary>
    public int Width { get; set; }

    /// <summary>Sheet height.</summary>
    public int Height { get; set; }

    /// <summary>Cell width.</summary>
    public int CellWidth { get; set; }

    /// <summary>Cell height.</summary>
    public int CellHeight { get; set; }

    /// <summary>Columns.</summary>
    public int Columns { get; set; }

    /// <summary>The cells in reading order.</summary>
    public List<ContactCellDto> Layout { get; set; } = [];

    /// <summary>The PNG in base64, only with <c>format=base64</c>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PngBase64 { get; set; }
}
