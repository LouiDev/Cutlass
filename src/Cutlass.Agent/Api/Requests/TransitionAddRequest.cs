namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/transition/add</c>.</summary>
public sealed class TransitionAddRequest : MutationRequest
{
    /// <summary>The track.</summary>
    public string? TrackId { get; set; }

    /// <summary>The cut frame.</summary>
    public long? CutFrame { get; set; }

    /// <summary>The kind (crossfade; audio tracks always use audioCrossfade).</summary>
    public string? Kind { get; set; }

    /// <summary>Length in frames.</summary>
    public long? DurationFrames { get; set; }
}
