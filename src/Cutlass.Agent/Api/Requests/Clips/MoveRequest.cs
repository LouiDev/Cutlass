namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/move</c>.</summary>
public sealed class MoveRequest : MutationRequest
{
    /// <summary>The clips.</summary>
    public List<string>? ClipIds { get; set; }

    /// <summary>Frames to move.</summary>
    public long? DeltaFrames { get; set; }

    /// <summary>Tracks to move.</summary>
    public int? DeltaTracks { get; set; }

    /// <summary><c>overwrite</c> or <c>insert</c>.</summary>
    public string? Mode { get; set; }

    /// <summary>Move linked partners too (true).</summary>
    public bool? IncludeLinked { get; set; }
}
