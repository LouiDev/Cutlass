namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>What a generator clip generates (effects batch 5), in the project summary.</summary>
public sealed class GeneratorDto
{
    /// <summary><c>colorMatte</c>, <c>bars</c> or <c>tone</c>.</summary>
    public string Kind { get; set; } = "colorMatte";

    /// <summary>The matte colour <c>#RRGGBB</c> (kept but unused by bars and the tone).</summary>
    public string Color { get; set; } = "#000000";

    /// <summary>The tone frequency in Hz (kept but unused by the video kinds).</summary>
    public double ToneHz { get; set; } = 1000;
}
