namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/trim</c>: <c>deltaFrames</c> or <c>toFrame</c>.</summary>
public sealed class TrimRequest : MutationRequest
{
    /// <summary>The clip.</summary>
    public string? ClipId { get; set; }

    /// <summary><c>start</c> or <c>end</c>.</summary>
    public string? Edge { get; set; }

    /// <summary>Frames to move the edge.</summary>
    public long? DeltaFrames { get; set; }

    /// <summary>The frame to move the edge to.</summary>
    public long? ToFrame { get; set; }

    /// <summary>Shift later clips.</summary>
    public bool? Ripple { get; set; }

    /// <summary>Trim linked partners too (true).</summary>
    public bool? IncludeLinked { get; set; }
}
