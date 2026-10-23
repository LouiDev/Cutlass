namespace Cutlass.Agent.Api.Dto.Status;

/// <summary>The result of <c>GET /v1/capabilities</c>.</summary>
public sealed class CapabilitiesDto
{
    /// <summary>The additive API revision.</summary>
    public int ApiRevision { get; set; } = ApiVersion.Revision;

    /// <summary>The export presets.</summary>
    public List<PresetDto> Presets { get; set; } = [];

    /// <summary>The export qualities.</summary>
    public List<string> Qualities { get; set; } = [];

    /// <summary>The transition kinds.</summary>
    public List<string> TransitionKinds { get; set; } = [];

    /// <summary>The title anchors.</summary>
    public List<string> TitleAnchors { get; set; } = [];

    /// <summary>The edit modes.</summary>
    public List<string> EditModes { get; set; } = [];

    /// <summary>True when titles render (ffmpeg has drawtext).</summary>
    public bool Titles { get; set; }

    /// <summary>The limits of section 4.6.</summary>
    public LimitsDto Limits { get; set; } = new();

    /// <summary>How events are delivered: <c>longpoll</c>.</summary>
    public string Events { get; set; } = "longpoll";
}
