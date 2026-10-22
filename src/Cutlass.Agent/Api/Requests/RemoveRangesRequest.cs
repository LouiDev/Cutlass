namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/remove-ranges</c>.</summary>
public sealed class RemoveRangesRequest : MutationRequest
{
    /// <summary>The ranges, at most 500.</summary>
    public List<FrameRange>? Ranges { get; set; }

    /// <summary>Only these tracks.</summary>
    public List<string>? TrackIds { get; set; }

    /// <summary>Close the gaps (true).</summary>
    public bool? Ripple { get; set; }
}
