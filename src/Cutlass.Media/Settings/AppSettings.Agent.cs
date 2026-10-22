using System.Text.Json.Serialization;

namespace Cutlass.Media.Settings;

/// <summary>The Settings > Agents page (AGENT_INTEGRATION 6.1).</summary>
public sealed partial record AppSettings
{
    /// <summary>The default agent port.</summary>
    public const int DefaultAgentPort = 47390;

    /// <summary>The default agent lease in seconds.</summary>
    public const int DefaultAgentLeaseSeconds = 90;

    /// <summary>The default longest lease an agent may request, in seconds.</summary>
    public const int DefaultAgentMaxLeaseSeconds = 300;

    // Value-equal lists keep the record comparable by value (FolderList).
    private IReadOnlyList<string> _agentReadableFolders = FolderList.Empty;
    private IReadOnlyList<string> _agentWritableFolders = FolderList.Empty;

    /// <summary>"Allow agent connections": the app serves the local agent API (off by default).</summary>
    public bool AgentConnectionsEnabled { get; set; }

    /// <summary>Alias of <see cref="AgentConnectionsEnabled"/> (not stored separately).</summary>
    [JsonIgnore]
    public bool AllowAgentConnections
    {
        get => AgentConnectionsEnabled;
        set => AgentConnectionsEnabled = value;
    }

    /// <summary>The loopback port of the agent API, 1024 to 65535 (47390 by default).</summary>
    public int AgentPort { get; set; } = DefaultAgentPort;

    /// <summary>The lease an agent gets unless it asks for another, in seconds (15 to 600, 90 by default).</summary>
    public int AgentLeaseSeconds { get; set; } = DefaultAgentLeaseSeconds;

    /// <summary>The longest lease an agent may request, in seconds (15 to 600, 300 by default).</summary>
    public int AgentMaxLeaseSeconds { get; set; } = DefaultAgentMaxLeaseSeconds;

    /// <summary>"Ask me before an agent takes the project" (off by default).</summary>
    public bool AgentAskFirst { get; set; }

    /// <summary>"Agent may read from": extra folders an agent may open, import and read fonts from.</summary>
    public IReadOnlyList<string> AgentReadableFolders
    {
        get => _agentReadableFolders;
        set => _agentReadableFolders = FolderList.From(value);
    }

    /// <summary>"Agent may write to": extra folders an agent may save projects and exports into.</summary>
    public IReadOnlyList<string> AgentWritableFolders
    {
        get => _agentWritableFolders;
        set => _agentWritableFolders = FolderList.From(value);
    }
}
