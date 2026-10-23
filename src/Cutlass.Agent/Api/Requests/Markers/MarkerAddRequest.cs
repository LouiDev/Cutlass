namespace Cutlass.Agent.Api.Requests.Markers;

/// <summary><c>POST /v1/edit/marker/add</c>.</summary>
public sealed class MarkerAddRequest : MutationRequest
{
    /// <summary>The frame.</summary>
    public long? Frame { get; set; }

    /// <summary>The label.</summary>
    public string? Name { get; set; }

    /// <summary>The colour.</summary>
    public string? Color { get; set; }
}
