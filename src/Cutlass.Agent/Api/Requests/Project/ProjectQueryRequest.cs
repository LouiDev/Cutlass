namespace Cutlass.Agent.Api.Requests.Project;

/// <summary><c>GET /v1/project</c> and <c>/v1/project/describe</c> filters.</summary>
public sealed class ProjectQueryRequest
{
    /// <summary>Only these tracks (the query form is <c>tracks=id,id</c>).</summary>
    public List<string>? TrackIds { get; set; }

    /// <summary>Only clips ending after this frame.</summary>
    public long? From { get; set; }

    /// <summary>Only clips starting before this frame.</summary>
    public long? To { get; set; }
}
