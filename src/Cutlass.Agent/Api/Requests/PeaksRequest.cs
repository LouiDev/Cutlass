namespace Cutlass.Agent.Api.Requests;

/// <summary><c>GET /v1/media/{id}/peaks</c>.</summary>
public sealed class PeaksRequest
{
    /// <summary>The media id.</summary>
    public string? MediaId { get; set; }

    /// <summary>Source start in flicks.</summary>
    public long? StartFlicks { get; set; }

    /// <summary>Source end in flicks.</summary>
    public long? EndFlicks { get; set; }

    /// <summary>Bins, at most 4000.</summary>
    public int? Bins { get; set; }
}
