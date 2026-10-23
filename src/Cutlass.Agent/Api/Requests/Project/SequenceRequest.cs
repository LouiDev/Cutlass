namespace Cutlass.Agent.Api.Requests.Project;

/// <summary><c>POST /v1/edit/sequence</c>: null fields keep their values.</summary>
public sealed class SequenceRequest : MutationRequest
{
    /// <summary>Canvas width.</summary>
    public int? Width { get; set; }

    /// <summary>Canvas height.</summary>
    public int? Height { get; set; }

    /// <summary>Frame rate such as <c>30000/1001</c>.</summary>
    public string? FrameRate { get; set; }
}
