namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/place</c>. Each <c>...Flicks</c> may be given as <c>...Seconds</c> instead, never both.</summary>
public sealed class PlaceRequest : MutationRequest
{
    /// <summary>The media id.</summary>
    public string? MediaId { get; set; }

    /// <summary>The timeline frame.</summary>
    public long? At { get; set; }

    /// <summary><c>overwrite</c> or <c>insert</c>.</summary>
    public string? Mode { get; set; }

    /// <summary><c>both</c>, <c>video</c> or <c>audio</c>.</summary>
    public string? Streams { get; set; }

    /// <summary>The video track, or null for the targeted one.</summary>
    public string? VideoTrackId { get; set; }

    /// <summary>The audio track, or null for the targeted one.</summary>
    public string? AudioTrackId { get; set; }

    /// <summary>Source in point in flicks.</summary>
    public long? SourceInFlicks { get; set; }

    /// <summary>Source in point in seconds.</summary>
    public double? SourceInSeconds { get; set; }

    /// <summary>Source out point in flicks.</summary>
    public long? SourceOutFlicks { get; set; }

    /// <summary>Source out point in seconds.</summary>
    public double? SourceOutSeconds { get; set; }

    /// <summary>Length in frames.</summary>
    public long? DurationFrames { get; set; }
}
