namespace Cutlass.Agent.Api.Requests.Markers;

/// <summary><c>POST /v1/edit/marker/remove</c>.</summary>
public sealed class MarkerIdsRequest : MutationRequest
{
    /// <summary>The markers.</summary>
    public List<string>? MarkerIds { get; set; }
}
