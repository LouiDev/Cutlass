namespace Cutlass.Agent.Api.Requests.Transitions;

/// <summary><c>POST /v1/edit/transition/update</c>.</summary>
public sealed class TransitionUpdateRequest : MutationRequest
{
    /// <summary>The transition.</summary>
    public string? TransitionId { get; set; }

    /// <summary>The new kind.</summary>
    public string? Kind { get; set; }

    /// <summary>The new length.</summary>
    public long? DurationFrames { get; set; }
}
