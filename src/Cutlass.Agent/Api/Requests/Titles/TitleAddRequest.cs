using Cutlass.Agent.Api.Dto.Project;

namespace Cutlass.Agent.Api.Requests.Titles;

/// <summary><c>POST /v1/edit/title/add</c>.</summary>
public sealed class TitleAddRequest : MutationRequest
{
    /// <summary>A video track.</summary>
    public string? TrackId { get; set; }

    /// <summary>The timeline frame.</summary>
    public long? At { get; set; }

    /// <summary>Length in frames.</summary>
    public long? DurationFrames { get; set; }

    /// <summary><c>overwrite</c> or <c>insert</c>.</summary>
    public string? Mode { get; set; }

    /// <summary>Text and style over the default title.</summary>
    public TitleDto? Title { get; set; }

    /// <summary>Opacity.</summary>
    public double? Opacity { get; set; }

    /// <summary>Fade-in frames.</summary>
    public long? FadeInFrames { get; set; }

    /// <summary>Fade-out frames.</summary>
    public long? FadeOutFrames { get; set; }
}
