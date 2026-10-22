namespace Cutlass.App.Services.Agent;

/// <summary>The state of the App's agent HTTP server (AGENT_INTEGRATION 6.1 status line).</summary>
public enum AgentServerStatus
{
    /// <summary>Connections are off.</summary>
    Off,

    /// <summary>The server is starting.</summary>
    Starting,

    /// <summary>The server listens (Success dot).</summary>
    Listening,

    /// <summary>Another live Cutlass process owns <c>endpoint.json</c> and serves agents.</summary>
    OtherWindow,

    /// <summary>The fixed port is taken (Danger).</summary>
    PortInUse,

    /// <summary>The server could not start for another reason, for example an http.sys URL ACL (Danger).</summary>
    Error,
}
