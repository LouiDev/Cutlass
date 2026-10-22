namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/track/add</c>.</summary>
public sealed class TrackAddRequest : MutationRequest
{
    /// <summary><c>video</c> or <c>audio</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>The kind index (1 is V1 or A1), or null for the outermost.</summary>
    public int? Index { get; set; }
}
