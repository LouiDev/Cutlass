namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/slip</c>.</summary>
public sealed class SlipRequest : MutationRequest
{
    /// <summary>The clip.</summary>
    public string? ClipId { get; set; }

    /// <summary>Frames to slip.</summary>
    public long? DeltaFrames { get; set; }

    /// <summary>Slip linked partners too (true).</summary>
    public bool? IncludeLinked { get; set; }
}
