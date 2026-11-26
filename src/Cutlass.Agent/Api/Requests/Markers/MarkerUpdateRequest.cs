namespace Cutlass.Agent.Api.Requests.Markers;

/// <summary><c>POST /v1/edit/marker/update</c>.</summary>
public sealed class MarkerUpdateRequest : MutationRequest
{
    /// <summary>The marker.</summary>
    public string? MarkerId { get; set; }

    /// <summary>The new frame.</summary>
    public long? Frame { get; set; }

    /// <summary>The new label.</summary>
    public string? Name { get; set; }

    /// <summary>The new colour.</summary>
    public string? Color { get; set; }
}
