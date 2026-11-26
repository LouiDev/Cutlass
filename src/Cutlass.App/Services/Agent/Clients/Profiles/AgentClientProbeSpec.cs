namespace Cutlass.App.Services.Agent.Clients.Profiles;

/// <summary>
/// How a command line client reports its registration (AGENT_INTEGRATION 9.3). The output is read tolerantly: any
/// <see cref="NotRegisteredWhen"/> text means not registered; the <see cref="ScopeField"/> and
/// <see cref="CommandField"/> lines (or a <see cref="ListPrefix"/> line of a list) give the scope and the command;
/// without them the text is searched for <see cref="RegisteredWhen"/>.
/// </summary>
/// <param name="Command">The tool's name on PATH.</param>
/// <param name="Args">The arguments of the check.</param>
/// <param name="FallbackArgs">Arguments tried when the check fails in an unexpected way (a list of every server), or null.</param>
/// <param name="RegisteredWhen">The text a current registration contains (usually <c>{exe}</c>).</param>
/// <param name="NotRegisteredWhen">Texts that mean the server is not registered.</param>
/// <param name="ScopeField">The start of the line that names the scope, or null.</param>
/// <param name="CommandField">The start of the line that names the command, or null.</param>
/// <param name="ListPrefix">The start of the server's line in a list (<c>name: command - health</c>), or null.</param>
public sealed record AgentClientProbeSpec(
    string Command,
    IReadOnlyList<string>? Args = null,
    IReadOnlyList<string>? FallbackArgs = null,
    string? RegisteredWhen = null,
    IReadOnlyList<string>? NotRegisteredWhen = null,
    string? ScopeField = null,
    string? CommandField = null,
    string? ListPrefix = null);
