namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/roll</c>.</summary>
public sealed class RollRequest : MutationRequest
{
    /// <summary>The outgoing clip.</summary>
    public string? LeftClipId { get; set; }

    /// <summary>The incoming clip.</summary>
    public string? RightClipId { get; set; }

    /// <summary>Frames to move the cut.</summary>
    public long? DeltaFrames { get; set; }

    /// <summary>Roll linked partners too (true).</summary>
    public bool? IncludeLinked { get; set; }
}
