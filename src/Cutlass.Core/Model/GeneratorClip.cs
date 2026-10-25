namespace Cutlass.Core.Model;

/// <summary>
/// A generated clip without media (effects batch 5): a colour matte or colour bars on a video track, or a sine tone on
/// an audio track. Generators are unbounded like stills, have no source position or speed, and are never linked.
/// </summary>
/// <param name="Id">Clip id (prefix <c>c_</c>).</param>
/// <param name="Start">First timeline frame.</param>
/// <param name="Duration">Length in frames, at least 1.</param>
/// <param name="Enabled">Disabled clips are not rendered.</param>
/// <param name="Kind">What the clip generates; decides the track kind it belongs on.</param>
/// <param name="Color">The matte colour as <c>#RRGGBB</c> (default <see cref="DefaultColor"/>; ignored by bars and the tone).</param>
/// <param name="ToneHz">The tone frequency in Hz, <see cref="MinToneHz"/> to <see cref="MaxToneHz"/> (default <see cref="DefaultToneHz"/>; ignored by video kinds).</param>
/// <param name="Video">Video properties of the matte and the bars (opacity, fades, transform, colour, effects).</param>
/// <param name="Audio">
/// Audio properties of the tone (gain, fades, keyframes, effects). The tone level is the sine source level (amplitude
/// 1/8 on each channel, -18 dBFS peak per channel) plus <see cref="AudioProps.GainDb"/>, so there is no separate level field.
/// </param>
public sealed record GeneratorClip(
    string Id,
    long Start,
    long Duration,
    bool Enabled,
    GeneratorKind Kind,
    string Color,
    double ToneHz,
    VideoProps Video,
    AudioProps Audio) : Clip(Id, Start, Duration, Enabled, null)
{
    /// <summary>The default matte colour, black.</summary>
    public const string DefaultColor = "#000000";

    /// <summary>The default tone frequency in Hz.</summary>
    public const double DefaultToneHz = 1000;

    /// <summary>The lowest tone frequency in Hz.</summary>
    public const double MinToneHz = 20;

    /// <summary>The highest tone frequency in Hz.</summary>
    public const double MaxToneHz = 20000;

    /// <summary>A new generator of the given kind with the default colour, frequency and neutral props.</summary>
    /// <param name="id">The clip id.</param>
    /// <param name="start">The first timeline frame.</param>
    /// <param name="duration">The length in frames.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The clip.</returns>
    public static GeneratorClip Create(string id, long start, long duration, GeneratorKind kind) =>
        new(id, start, duration, true, kind, DefaultColor, DefaultToneHz, VideoProps.Default, AudioProps.Default);
}
