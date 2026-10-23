namespace Cutlass.Agent.Api.Requests.Media;

/// <summary><c>POST /v1/audio/silences</c>.</summary>
public sealed class SilencesRequest
{
    /// <summary>First frame (0).</summary>
    public long? Start { get; set; }

    /// <summary>End frame (the sequence end).</summary>
    public long? End { get; set; }

    /// <summary>Threshold in dBFS (-40).</summary>
    public double? ThresholdDb { get; set; }

    /// <summary>Shortest silence in ms (500).</summary>
    public int? MinDurationMs { get; set; }

    /// <summary>Sound kept on each side in ms (100).</summary>
    public int? PadMs { get; set; }

    /// <summary>Only these audio tracks.</summary>
    public List<string>? TrackIds { get; set; }
}
