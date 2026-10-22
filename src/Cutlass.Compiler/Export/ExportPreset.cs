using System.Collections.Immutable;
using Cutlass.Compiler.Capabilities;

namespace Cutlass.Compiler.Export;

/// <summary>
/// One export preset of DESIGN 9 with the exact encoder arguments of TECH_BRIEF 9. Encoder lists are in preference
/// order: the first available one is used, later ones are fallbacks.
/// </summary>
/// <param name="Id">A stable id, for example <c>h264</c> (settings and tests refer to it).</param>
/// <param name="DisplayName">The name shown in the export dialog, for example <c>H.264</c>.</param>
/// <param name="Extension">The file extension including the dot, for example <c>.mp4</c>.</param>
/// <param name="Kind">Video, GIF or audio only.</param>
/// <param name="PixelFormat">The output <c>-pix_fmt</c>, which the graph tail's <c>format=</c> matches; null for
/// GIF and audio-only presets.</param>
/// <param name="VideoEncoders">The video encoder choices, empty for audio-only presets.</param>
/// <param name="AudioEncoders">The audio encoder choices, empty for GIF.</param>
/// <param name="ContainerArguments">Muxer arguments written after the codec arguments, for example
/// <c>-movflags +faststart</c> or <c>-loop 0</c>.</param>
/// <param name="QualityValues">The encoder value for Draft, Good, High and Best, or empty for a fixed quality.</param>
/// <param name="RequiredFilters">Filters the preset needs beyond the required set, for example <c>palettegen</c>.</param>
public sealed record ExportPreset(
    string Id,
    string DisplayName,
    string Extension,
    ExportPresetKind Kind,
    string? PixelFormat,
    ImmutableArray<EncoderTemplate> VideoEncoders,
    ImmutableArray<EncoderTemplate> AudioEncoders,
    ImmutableArray<string> ContainerArguments,
    ImmutableArray<int> QualityValues,
    ImmutableArray<string> RequiredFilters)
{
    /// <summary>True when the preset writes video (everything except the audio-only presets).</summary>
    public bool HasVideo => Kind != ExportPresetKind.Audio;

    /// <summary>True when the preset writes audio (everything except GIF).</summary>
    public bool HasAudio => Kind != ExportPresetKind.Gif;

    /// <summary>True when the quality choice changes the encoder settings.</summary>
    public bool HasQuality => !QualityValues.IsDefaultOrEmpty;

    /// <summary>The graph tail matching this preset.</summary>
    public OutputTail Tail => Kind == ExportPresetKind.Gif
        ? OutputTail.Gif
        : OutputTail.ForExport(PixelFormat ?? "yuv420p");

    /// <summary>The encoder value for a quality choice (CRF, or <c>-q:a</c> for MP3).</summary>
    /// <param name="quality">The quality.</param>
    /// <returns>The value, or null when the preset has a fixed quality.</returns>
    public int? QualityValue(ExportQuality quality) =>
        HasQuality ? QualityValues[Math.Clamp((int)quality, 0, QualityValues.Length - 1)] : null;

    /// <summary>
    /// Explains why the preset is greyed out on this ffmpeg build, for example
    /// <c>libx265 is not in this ffmpeg build</c>.
    /// </summary>
    /// <param name="capabilities">The detected capabilities.</param>
    /// <returns>The reason, or null when the preset is available.</returns>
    public string? UnavailableReason(FfmpegCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        foreach (var filter in RequiredFilters)
        {
            if (!capabilities.HasFilter(filter))
            {
                return $"{filter} is not in this ffmpeg build";
            }
        }

        return MissingReason(VideoEncoders, capabilities) ?? MissingReason(AudioEncoders, capabilities);
    }

    /// <summary>True when the preset can run on this ffmpeg build.</summary>
    /// <param name="capabilities">The detected capabilities.</param>
    /// <returns>True when available.</returns>
    public bool IsAvailable(FfmpegCapabilities capabilities) => UnavailableReason(capabilities) is null;

    /// <summary>Picks the first available video and audio encoder.</summary>
    /// <param name="capabilities">The detected capabilities.</param>
    /// <returns>The encoding, or null when the preset is unavailable.</returns>
    public ExportEncoding? Resolve(FfmpegCapabilities capabilities)
    {
        if (!IsAvailable(capabilities))
        {
            return null;
        }

        return new ExportEncoding(this, Pick(VideoEncoders, capabilities), Pick(AudioEncoders, capabilities));
    }

    private static EncoderTemplate? Pick(ImmutableArray<EncoderTemplate> choices, FfmpegCapabilities capabilities)
    {
        if (choices.IsDefaultOrEmpty)
        {
            return null;
        }

        foreach (var choice in choices)
        {
            if (choice.AlwaysAvailable || capabilities.HasEncoder(choice.Encoder))
            {
                return choice;
            }
        }

        return null;
    }

    private static string? MissingReason(ImmutableArray<EncoderTemplate> choices, FfmpegCapabilities capabilities)
    {
        if (choices.IsDefaultOrEmpty || Pick(choices, capabilities) is not null)
        {
            return null;
        }

        var names = choices.Select(c => c.Encoder).Distinct(StringComparer.Ordinal).ToArray();
        return names.Length == 1
            ? $"{names[0]} is not in this ffmpeg build"
            : $"none of {string.Join(", ", names)} is in this ffmpeg build";
    }
}
