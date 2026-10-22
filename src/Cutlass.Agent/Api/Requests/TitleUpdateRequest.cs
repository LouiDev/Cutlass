using Cutlass.Agent.Api.Dto;

namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/title/update</c>: null fields keep their values.</summary>
public sealed class TitleUpdateRequest : MutationRequest
{
    /// <summary>The title clip.</summary>
    public string? ClipId { get; set; }

    /// <summary>The fields to change.</summary>
    public TitleDto? Title { get; set; }

    /// <summary>Opacity.</summary>
    public double? Opacity { get; set; }

    /// <summary>Fade-in frames.</summary>
    public long? FadeInFrames { get; set; }

    /// <summary>Fade-out frames.</summary>
    public long? FadeOutFrames { get; set; }
}
