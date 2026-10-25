namespace Cutlass.Agent.Api.Requests;

/// <summary><c>POST /v1/edit/generator/add</c> (effects batch 5): a colour matte, bars or a tone.</summary>
public sealed class GeneratorAddRequest : MutationRequest
{
    /// <summary>A video track for <c>colorMatte</c> and <c>bars</c>, an audio track for <c>tone</c>.</summary>
    public string? TrackId { get; set; }

    /// <summary>The timeline frame.</summary>
    public long? At { get; set; }

    /// <summary>Length in frames.</summary>
    public long? DurationFrames { get; set; }

    /// <summary><c>overwrite</c> or <c>insert</c>.</summary>
    public string? Mode { get; set; }

    /// <summary><c>colorMatte</c>, <c>bars</c> or <c>tone</c>.</summary>
    public string? Kind { get; set; }

    /// <summary>The matte colour <c>#RRGGBB</c> (default black).</summary>
    public string? Color { get; set; }

    /// <summary>The tone frequency in Hz, 20 to 20000 (default 1000).</summary>
    public double? ToneHz { get; set; }

    /// <summary>Video generators: opacity.</summary>
    public double? Opacity { get; set; }

    /// <summary>Tone: gain in dB over the sine level (-18 dBFS peak per channel).</summary>
    public double? GainDb { get; set; }

    /// <summary>Fade-in frames.</summary>
    public long? FadeInFrames { get; set; }

    /// <summary>Fade-out frames.</summary>
    public long? FadeOutFrames { get; set; }
}
