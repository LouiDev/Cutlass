namespace Cutlass.Agent.Api.Requests.Preview;

/// <summary><c>POST /v1/contact-sheet</c>: either <c>frames</c> or <c>start</c>, <c>end</c> and <c>count</c>.</summary>
public sealed class ContactSheetRequest
{
    /// <summary>The frames, at most 64.</summary>
    public List<long>? Frames { get; set; }

    /// <summary>First frame of an even spread.</summary>
    public long? Start { get; set; }

    /// <summary>End frame of the spread (exclusive).</summary>
    public long? End { get; set; }

    /// <summary>Cells of the spread, at most 64.</summary>
    public int? Count { get; set; }

    /// <summary>Cell width (320).</summary>
    public int? CellWidth { get; set; }

    /// <summary>Columns, or null for a near square grid.</summary>
    public int? Columns { get; set; }

    /// <summary><c>png</c> or <c>base64</c>.</summary>
    public string? Format { get; set; }
}
