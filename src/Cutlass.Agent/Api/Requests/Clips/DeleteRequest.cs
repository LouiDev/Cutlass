namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/delete</c>.</summary>
public sealed class DeleteRequest : MutationRequest
{
    /// <summary>The clips.</summary>
    public List<string>? ClipIds { get; set; }

    /// <summary>Close the gaps.</summary>
    public bool? Ripple { get; set; }

    /// <summary>Delete linked partners too (true).</summary>
    public bool? IncludeLinked { get; set; }
}
