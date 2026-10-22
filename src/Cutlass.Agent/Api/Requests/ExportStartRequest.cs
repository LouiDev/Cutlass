using System.Text.Json;

namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/exports</c>.</summary>
public sealed class ExportStartRequest
{
    /// <summary>The preset id, for example <c>h264</c>.</summary>
    public string? Preset { get; set; }

    /// <summary><c>draft</c>, <c>good</c>, <c>high</c> or <c>best</c>.</summary>
    public string? Quality { get; set; }

    /// <summary>The output height.</summary>
    public int? Height { get; set; }

    /// <summary>The GIF width.</summary>
    public int? Width { get; set; }

    /// <summary><c>"whole"</c>, <c>"inOut"</c> or <c>{start, end}</c>.</summary>
    public JsonElement? Range { get; set; }

    /// <summary>First frame of a custom range (alternative to an object range).</summary>
    public long? Start { get; set; }

    /// <summary>End frame of a custom range.</summary>
    public long? End { get; set; }

    /// <summary>The output file.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Peak limiter (true).</summary>
    public bool? LimitPeaks { get; set; }

    /// <summary>Replace an existing file (false).</summary>
    public bool? Overwrite { get; set; }
}
