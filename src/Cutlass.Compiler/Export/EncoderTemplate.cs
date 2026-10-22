using System.Collections.Immutable;
using Cutlass.Compiler.Graph;

namespace Cutlass.Compiler.Export;

/// <summary>
/// The encoder arguments of one preset choice, exactly as in TECH_BRIEF 9, with placeholders: <c>{q}</c> for the
/// quality value and <c>{bitrate}</c> for a bitrate in bits per second.
/// </summary>
/// <param name="Encoder">The ffmpeg encoder name the choice needs, for example <c>libx264</c>.</param>
/// <param name="Arguments">The arguments separated by single spaces, for example
/// <c>-c:v libx264 -preset medium -crf {q}</c>. Empty for none.</param>
/// <param name="AlwaysAvailable">True when the preset does not gate on this encoder (native encoders such as
/// <c>pcm_s16le</c>, <c>flac</c>, the video presets' <c>aac</c> and <c>gif</c>).</param>
public sealed record EncoderTemplate(string Encoder, string Arguments, bool AlwaysAvailable = false)
{
    /// <summary>The quality placeholder.</summary>
    public const string QualityPlaceholder = "{q}";

    /// <summary>The bitrate placeholder.</summary>
    public const string BitratePlaceholder = "{bitrate}";

    /// <summary>Expands the arguments.</summary>
    /// <param name="quality">The value for <c>{q}</c>, if the template uses it.</param>
    /// <param name="bitrate">The value for <c>{bitrate}</c> in bits per second, if the template uses it.</param>
    /// <returns>The arguments, one list element per argument.</returns>
    /// <exception cref="InvalidOperationException">When a placeholder has no value.</exception>
    public ImmutableArray<string> Expand(int? quality, long? bitrate)
    {
        if (string.IsNullOrEmpty(Arguments))
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var token in Arguments.Split(' '))
        {
            builder.Add(token switch
            {
                QualityPlaceholder => FfNum.Integer(quality
                    ?? throw new InvalidOperationException($"The {Encoder} arguments need a quality value.")),
                BitratePlaceholder => FfNum.Integer(bitrate
                    ?? throw new InvalidOperationException($"The {Encoder} arguments need a bitrate.")),
                _ => token,
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>True when the template uses the bitrate placeholder.</summary>
    public bool UsesBitrate => Arguments.Split(' ').Contains(BitratePlaceholder);
}
