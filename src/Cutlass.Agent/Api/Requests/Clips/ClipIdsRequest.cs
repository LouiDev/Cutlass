namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/link</c> and <c>unlink</c>.</summary>
public sealed class ClipIdsRequest : MutationRequest
{
    /// <summary>The clips.</summary>
    public List<string>? ClipIds { get; set; }
}
