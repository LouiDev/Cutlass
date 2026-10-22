using System.Globalization;
using System.Net;
using Cutlass.Agent.Http;

namespace Cutlass.App.Services.Agent;

/// <summary>
/// The listener part of <see cref="AgentServerController"/>: <see cref="AgentHttpServer"/> binds
/// <c>localhost</c> and, when http.sys refuses it on Windows, <c>127.0.0.1</c>; an error keeps its
/// <c>netsh http add urlacl</c> hint for the Settings status line (AGENT_INTEGRATION 8).
/// </summary>
public sealed partial class AgentServerController
{
    private AgentHttpServer? _server;

    /// <summary>True while the HTTP server listens.</summary>
    public bool IsRunning => _server?.IsRunning == true;

    // Starts the server on the first usable port. Returns the base URL, or the status and message of the failure.
    private async Task<(string? BaseUrl, AgentServerStatus Status, string Message)> ListenAsync(int[] ports, string token)
    {
        if (_dispatcher is not { } dispatcher || ports.Length == 0)
        {
            return (null, AgentServerStatus.Error, "The agent server has no port to use.");
        }

        var server = new AgentHttpServer(dispatcher, token);
        try
        {
            await server.StartAsync(ports[0], fixedPort: ports.Length == 1);
        }
        catch (HttpListenerException ex)
        {
            await server.DisposeAsync();
            var inUse = ex.ErrorCode is 32 or 183 or 10048 or 98;
            var message = inUse && ports.Length == 1
                ? string.Create(CultureInfo.InvariantCulture, $"Port {ports[0]} is in use")
                : ex.Message;
            return (null, inUse ? AgentServerStatus.PortInUse : AgentServerStatus.Error, message);
        }

        _server = server;
        return (server.BaseUrl, AgentServerStatus.Listening, string.Empty);
    }

    private async Task StopListeningAsync()
    {
        if (_server is { } server)
        {
            _server = null;
            await server.DisposeAsync();
        }
    }

    // "Regenerate token": the running server takes the new token at once (no restart, no dropped agents but the old token).
    private void UseToken(string token)
    {
        if (_server is { } server)
        {
            server.Token = token;
        }
    }
}
