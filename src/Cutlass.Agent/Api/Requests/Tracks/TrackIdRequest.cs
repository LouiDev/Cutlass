namespace Cutlass.Agent.Api.Requests.Tracks;

/// <summary><c>POST /v1/edit/track/remove</c>.</summary>
public sealed class TrackIdRequest : MutationRequest
{
    /// <summary>The track.</summary>
    public string? TrackId { get; set; }
}
