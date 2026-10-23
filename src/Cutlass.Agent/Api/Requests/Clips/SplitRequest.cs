namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/split</c>.</summary>
public sealed class SplitRequest : MutationRequest
{
    /// <summary>The frame.</summary>
    public long? Frame { get; set; }

    /// <summary>Only these tracks.</summary>
    public List<string>? TrackIds { get; set; }

    /// <summary>Only these clips.</summary>
    public List<string>? ClipIds { get; set; }

    /// <summary>Split linked partners too (true).</summary>
    public bool? IncludeLinked { get; set; }
}
