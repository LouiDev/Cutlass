namespace Cutlass.Agent.Api.Requests.Media;

/// <summary><c>POST /v1/audio/levels</c>.</summary>
public sealed class AudioLevelsRequest
{
    /// <summary>First frame.</summary>
    public long? Start { get; set; }

    /// <summary>End frame (exclusive).</summary>
    public long? End { get; set; }

    /// <summary>The interval, 10 to 1000 ms (50).</summary>
    public int? IntervalMs { get; set; }

    /// <summary>Only these audio tracks.</summary>
    public List<string>? TrackIds { get; set; }
}
