using Cutlass.Agent.Http;
using Cutlass.Media.Platform;

namespace Cutlass.Agent.Mcp.Backend;

/// <summary>
/// Finds the running app's agent API (docs/AGENT_INTEGRATION.md 5.1): reads <c>&lt;data&gt;/agent/endpoint.json</c>,
/// checks that the pid and start time it names are alive, and reads the token file it points to. Nothing is cached:
/// <see cref="HttpBackend"/> calls <see cref="Locate"/> again after any connection failure, so a restarted app (new
/// port, new token) is picked up.
/// </summary>
public sealed class EndpointLocator
{
    private readonly EndpointFile _file;

    /// <summary>Creates a locator over an agent folder.</summary>
    /// <param name="agentDirectory">The agent folder, normally <c>AppPaths.ForCurrentPlatform().AgentDirectory</c>.</param>
    public EndpointLocator(string agentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentDirectory);
        AgentDirectory = agentDirectory;
        _file = new EndpointFile(agentDirectory);
    }

    /// <summary>The agent folder.</summary>
    public string AgentDirectory { get; }

    /// <summary>A locator for the current user's data folder.</summary>
    /// <returns>The locator.</returns>
    public static EndpointLocator ForCurrentUser() => new(AppPaths.ForCurrentPlatform().AgentDirectory);

    /// <summary>Reads the endpoint and its token.</summary>
    /// <returns>The live endpoint, or null when no live process serves agents or its token cannot be read.</returns>
    public LocatedEndpoint? Locate()
    {
        var info = _file.Read();
        if (info is null || string.IsNullOrWhiteSpace(info.BaseUrl) || !EndpointFile.IsLiveOther(info))
        {
            return null;
        }

        try
        {
            var token = File.ReadAllText(info.TokenFile).Trim();
            return token.Length == 0 ? null : new LocatedEndpoint(info, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
