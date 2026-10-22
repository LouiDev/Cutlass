using Cutlass.App.Services.Agent;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// An agent server for the Agent menu tests (AGENT_INTEGRATION 6.6): its status, the session switch and the paths
/// are plain properties, and Start/Stop record the calls instead of listening.
/// </summary>
internal sealed class FakeAgentServer : IAgentSettingsHost, IAgentServerSwitch
{
    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public AgentServerStatus Status { get; private set; } = AgentServerStatus.Off;

    /// <inheritdoc />
    public string StatusText { get; private set; } = "Off";

    /// <inheritdoc />
    public bool IsForcedOn => false;

    /// <inheritdoc />
    public string McpExecutablePath { get; set; } = "C:/Program Files/Cutlass/Cutlass.Agent.Mcp.exe";

    /// <inheritdoc />
    public string AgentDocsPath { get; set; } = "C:/Program Files/Cutlass/AGENT_INTEGRATION.md";

    /// <inheritdoc />
    public bool IsSwitchedOn { get; set; }

    /// <inheritdoc />
    public string? BaseUrl { get; set; }

    /// <inheritdoc />
    public string ExpectedBaseUrl => BaseUrl ?? "http://localhost:47390/";

    /// <inheritdoc />
    public string TokenFile { get; set; } = "C:/Users/me/AppData/Local/Cutlass/agent/token";

    /// <summary>The token <see cref="ReadTokenAsync"/> returns.</summary>
    public string Token { get; set; } = "tok_abcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>The Start and Stop calls in order: "start", "start keep" or "stop".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Sets the status and raises <see cref="StatusChanged"/>.</summary>
    /// <param name="status">The status.</param>
    /// <param name="text">The status line.</param>
    public void Set(AgentServerStatus status, string text)
    {
        Status = status;
        StatusText = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public Task<string> ReadTokenAsync(CancellationToken cancellationToken) => Task.FromResult(Token);

    /// <inheritdoc />
    public Task<string> RegenerateTokenAsync(CancellationToken cancellationToken) => Task.FromResult(Token);

    /// <inheritdoc />
    public Task StartForSessionAsync(bool keepOn)
    {
        Calls.Add(keepOn ? "start keep" : "start");
        IsSwitchedOn = true;
        BaseUrl = "http://localhost:47390/";
        Set(AgentServerStatus.Listening, "Listening on " + BaseUrl);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopForSessionAsync()
    {
        Calls.Add("stop");
        IsSwitchedOn = false;
        BaseUrl = null;
        Set(AgentServerStatus.Off, "Off");
        return Task.CompletedTask;
    }
}
