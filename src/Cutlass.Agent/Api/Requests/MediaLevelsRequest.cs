namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/media/{id}/levels</c>. Each <c>...Flicks</c> may be given as <c>...Seconds</c> instead, never both.</summary>
public sealed class MediaLevelsRequest
{
    /// <summary>The media id.</summary>
    public string? MediaId { get; set; }

    /// <summary>Source start in flicks.</summary>
    public long? StartFlicks { get; set; }

    /// <summary>Source start in seconds.</summary>
    public double? StartSeconds { get; set; }

    /// <summary>Source end in flicks.</summary>
    public long? EndFlicks { get; set; }

    /// <summary>Source end in seconds.</summary>
    public double? EndSeconds { get; set; }

    /// <summary>The interval, 10 to 1000 ms.</summary>
    public int? IntervalMs { get; set; }
}
