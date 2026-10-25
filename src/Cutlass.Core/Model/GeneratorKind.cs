namespace Cutlass.Core.Model;

/// <summary>
/// What a <see cref="GeneratorClip"/> generates (effects batch 5). Members are persisted by name, so they are only ever
/// appended, never renamed. <see cref="GeneratorKinds"/> tells video kinds from audio kinds.
/// </summary>
public enum GeneratorKind
{
    /// <summary>A solid colour filling the canvas (video tracks only).</summary>
    ColorMatte,

    /// <summary>SMPTE HD colour bars filling the canvas (video tracks only).</summary>
    Bars,

    /// <summary>A sine tone, 48 kHz stereo (audio tracks only).</summary>
    Tone,
}
