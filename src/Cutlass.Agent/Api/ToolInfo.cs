namespace Cutlass.Agent.Api;

/// <summary>One tool of the agent API (AGENT_INTEGRATION 2.1 and 4.4), shared by HTTP and MCP.</summary>
/// <param name="Name">The catalog name used by <c>/v1/call</c>, for example <c>edit.split</c>.</param>
/// <param name="McpName">The MCP tool name, for example <c>cutlass_split</c>, or null when MCP does not expose it.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Route">The route template; <c>{name}</c> segments become arguments of that name.</param>
/// <param name="NeedsLock">True when the call needs the holder's <c>X-Cutlass-Lock</c>.</param>
/// <param name="Mutating">True for model edits: they return the mutation envelope and accept <c>ifRevision</c> and <c>returnProject</c>.</param>
/// <param name="Heavy">True for renders and analysis: at most 2 run at once (1 while the user plays).</param>
/// <param name="Result">What the call returns: JSON, PNG or text.</param>
/// <param name="Description">The model-facing description (under 300 characters).</param>
/// <param name="Schema">The JSON schema of the arguments, as JSON text.</param>
public sealed record ToolInfo(
    string Name,
    string? McpName,
    string Method,
    string Route,
    bool NeedsLock,
    bool Mutating,
    bool Heavy,
    ToolResultKind Result,
    string Description,
    string Schema);
