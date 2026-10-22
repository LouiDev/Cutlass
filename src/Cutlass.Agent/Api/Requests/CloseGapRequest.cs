namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/close-gap</c>.</summary>
public sealed class CloseGapRequest : MutationRequest
{
    /// <summary>The track.</summary>
    public string? TrackId { get; set; }

    /// <summary>A frame inside or at the start of the gap.</summary>
    public long? GapStart { get; set; }
}
