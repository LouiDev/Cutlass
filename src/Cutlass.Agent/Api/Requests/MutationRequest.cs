namespace Cutlass.Agent.Api.Requests;

/// <summary>The fields every mutating call accepts (AGENT_INTEGRATION 4.2).</summary>
public class MutationRequest
{
    /// <summary>Fail with <c>conflict</c> when the revision is no longer this.</summary>
    public long? IfRevision { get; set; }

    /// <summary>Also return the project summary.</summary>
    public bool? ReturnProject { get; set; }
}
