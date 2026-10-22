namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/batch/begin</c> (with a label), <c>end</c>, <c>cancel</c>, <c>/v1/undo</c> and <c>/v1/redo</c> (empty).</summary>
public sealed class BatchRequest : MutationRequest
{
    /// <summary>The batch label (begin only).</summary>
    public string? Label { get; set; }
}
