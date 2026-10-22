namespace Cutlass.Agent.Tools;

/// <summary>One recorded agent call (AGENT_INTEGRATION 4.6). Never holds tokens or bodies.</summary>
/// <param name="TimeUtc">When the call finished.</param>
/// <param name="Agent">The agent's name (lock holder or <c>X-Cutlass-Agent</c>), or <c>?</c>.</param>
/// <param name="Tool">The catalog tool name, for example <c>edit.split</c>.</param>
/// <param name="DurationMs">How long it took.</param>
/// <param name="Code"><c>ok</c> or the error code, for example <c>lock_required</c>.</param>
/// <param name="Revision">The revision after the call.</param>
/// <param name="Arguments">A one-line summary of the arguments (at most 200 characters).</param>
public sealed record AuditEntry(DateTime TimeUtc, string Agent, string Tool, long DurationMs, string Code, long Revision, string Arguments)
{
    /// <summary>True when the call failed.</summary>
    public bool IsError => !string.Equals(Code, "ok", StringComparison.Ordinal);
}
