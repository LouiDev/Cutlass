namespace Cutlass.Agent.Api.Requests.Generators;

/// <summary><c>POST /v1/edit/generator/update</c>: null fields keep their values.</summary>
public sealed class GeneratorUpdateRequest : MutationRequest
{
    /// <summary>The generator clip.</summary>
    public string? ClipId { get; set; }

    /// <summary>A new kind on the same kind of track (<c>colorMatte</c> and <c>bars</c> swap; <c>tone</c> stays a tone).</summary>
    public string? Kind { get; set; }

    /// <summary>The matte colour <c>#RRGGBB</c>.</summary>
    public string? Color { get; set; }

    /// <summary>The tone frequency in Hz, 20 to 20000.</summary>
    public double? ToneHz { get; set; }

    /// <summary>Video generators: opacity.</summary>
    public double? Opacity { get; set; }

    /// <summary>Tone: gain in dB.</summary>
    public double? GainDb { get; set; }

    /// <summary>Fade-in frames.</summary>
    public long? FadeInFrames { get; set; }

    /// <summary>Fade-out frames.</summary>
    public long? FadeOutFrames { get; set; }
}
