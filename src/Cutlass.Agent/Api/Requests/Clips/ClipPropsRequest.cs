using Cutlass.Agent.Api.Dto.Project;

namespace Cutlass.Agent.Api.Requests.Clips;

/// <summary><c>POST /v1/edit/clip-props</c>: null fields keep their values.</summary>
public sealed class ClipPropsRequest : MutationRequest
{
    /// <summary>The clips.</summary>
    public List<string>? ClipIds { get; set; }

    /// <summary>Enable or disable.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Opacity (video and titles).</summary>
    public double? Opacity { get; set; }

    /// <summary>Transform fields (video).</summary>
    public TransformDto? Transform { get; set; }

    /// <summary>Colour fields (video).</summary>
    public ColorDto? Color { get; set; }

    /// <summary>Gain in dB (audio).</summary>
    public double? GainDb { get; set; }

    /// <summary>Fade-in frames.</summary>
    public long? FadeInFrames { get; set; }

    /// <summary>Fade-out frames.</summary>
    public long? FadeOutFrames { get; set; }
}
