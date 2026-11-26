namespace Cutlass.Agent.Api.Dto.Status;

/// <summary>The result of <c>GET /v1/status</c> (AGENT_INTEGRATION 4.4).</summary>
public sealed class StatusDto
{
    /// <summary>Always <c>Cutlass</c>.</summary>
    public string App { get; set; } = "Cutlass";

    /// <summary>The app version.</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>The API major version.</summary>
    public int ApiVersion { get; set; } = Api.ApiVersion.Current;

    /// <summary><c>app</c> or <c>headless</c>.</summary>
    public string HostKind { get; set; } = "app";

    /// <summary>The ffmpeg state.</summary>
    public FfmpegStatusDto Ffmpeg { get; set; } = new();

    /// <summary>The project state.</summary>
    public ProjectStatusDto Project { get; set; } = new();

    /// <summary>The lock state.</summary>
    public LockStateDto Lock { get; set; } = new();

    /// <summary>The user's playhead.</summary>
    public long Playhead { get; set; }

    /// <summary>The open batch label, or null.</summary>
    public string? Batch { get; set; }

    /// <summary>The revision.</summary>
    public long Revision { get; set; }
}
