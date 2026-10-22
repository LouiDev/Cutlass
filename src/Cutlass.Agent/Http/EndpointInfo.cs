namespace Cutlass.Agent.Http;

/// <summary>The content of <c>endpoint.json</c> (AGENT_INTEGRATION 4.1).</summary>
public sealed class EndpointInfo
{
    /// <summary>The API major version.</summary>
    public int ApiVersion { get; set; } = Api.ApiVersion.Current;

    /// <summary>The base URL clients use verbatim, for example <c>http://localhost:47390/</c>.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The serving process id.</summary>
    public int Pid { get; set; }

    /// <summary>The serving process start time (UTC).</summary>
    public DateTime ProcessStartUtc { get; set; }

    /// <summary><c>app</c> or <c>headless</c>.</summary>
    public string HostKind { get; set; } = "app";

    /// <summary>The token file, with forward slashes.</summary>
    public string TokenFile { get; set; } = string.Empty;

    /// <summary>The open project, or null while untitled.</summary>
    public string? ProjectPath { get; set; }
}
