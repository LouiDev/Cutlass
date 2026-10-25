namespace Cutlass.Core.Model;

/// <summary>Helpers over <see cref="GeneratorKind"/>: which track kind a generator belongs on and its display name.</summary>
public static class GeneratorKinds
{
    /// <summary>True for the generators that produce a picture and go on video tracks.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>True for <see cref="GeneratorKind.ColorMatte"/> and <see cref="GeneratorKind.Bars"/>.</returns>
    public static bool IsVideo(this GeneratorKind kind) => kind is GeneratorKind.ColorMatte or GeneratorKind.Bars;

    /// <summary>True for the generators that produce sound and go on audio tracks.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>True for <see cref="GeneratorKind.Tone"/>.</returns>
    public static bool IsAudio(this GeneratorKind kind) => kind is GeneratorKind.Tone;

    /// <summary>The kind of track a generator of this kind belongs on.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><see cref="TrackKind.Audio"/> for audio generators, otherwise <see cref="TrackKind.Video"/>.</returns>
    public static TrackKind TrackKindOf(this GeneratorKind kind) => kind.IsAudio() ? TrackKind.Audio : TrackKind.Video;

    /// <summary>The user-facing name: "Color Matte", "Bars" or "Tone".</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name.</returns>
    public static string DisplayName(this GeneratorKind kind) => kind switch
    {
        GeneratorKind.ColorMatte => "Color Matte",
        GeneratorKind.Bars => "Bars",
        GeneratorKind.Tone => "Tone",
        _ => kind.ToString(),
    };
}
