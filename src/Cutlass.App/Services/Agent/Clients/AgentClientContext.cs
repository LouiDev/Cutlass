namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// The live values a registration is filled with (AGENT_INTEGRATION 9.3): the MCP exe (<c>{exe}</c>), the server's
/// base URL (<c>{url}</c>), the token (<c>{token}</c>, only after the user confirmed it, and only for http transports;
/// otherwise the text names the token file) and the folder a command line client runs in (local scope registrations
/// belong to it).
/// </summary>
/// <param name="Exe">The MCP exe.</param>
/// <param name="Url">The agent server's base URL, ending with a slash.</param>
/// <param name="TokenFile">The token file.</param>
/// <param name="Token">The confirmed token, or null.</param>
/// <param name="WorkingDirectory">Where a client CLI runs, or null.</param>
public sealed record AgentClientContext(string Exe, string Url, string TokenFile, string? Token = null, string? WorkingDirectory = null)
{
    /// <summary>The <c>{token}</c> text: the token itself after the confirmation, else where to read it.</summary>
    public string TokenText => Token is { Length: > 0 } token ? token : "the content of " + TokenFile;
}
