namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/transition/remove</c>.</summary>
public sealed class TransitionIdsRequest : MutationRequest
{
    /// <summary>The transitions.</summary>
    public List<string>? TransitionIds { get; set; }
}
