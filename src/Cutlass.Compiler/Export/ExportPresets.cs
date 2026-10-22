using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Compiler.Export;

/// <summary>
/// Every export preset of DESIGN 9, with the arguments of TECH_BRIEF 9, plus the resolution helpers of the export
/// dialog.
/// </summary>
public static class ExportPresets
{
    private static readonly EncoderTemplate Aac192 = new("aac", "-c:a aac -b:a 192k", AlwaysAvailable: true);

    /// <summary>H.264 in MP4: x264 CRF, falling back to an OpenH264 bitrate; AAC 192k. The default preset.</summary>
    public static ExportPreset H264 { get; } = new(
        "h264",
        "H.264",
        ".mp4",
        ExportPresetKind.Video,
        "yuv420p",
        [
            new EncoderTemplate("libx264", "-c:v libx264 -preset medium -crf {q} -pix_fmt yuv420p -profile:v high"),
            new EncoderTemplate("libopenh264", "-c:v libopenh264 -b:v {bitrate} -pix_fmt yuv420p -profile:v high"),
        ],
        [Aac192],
        ["-movflags", "+faststart"],
        [28, 23, 20, 17],
        []);

    /// <summary>H.265 in MP4: x265 CRF with the <c>hvc1</c> tag; AAC 192k.</summary>
    public static ExportPreset H265 { get; } = new(
        "h265",
        "H.265",
        ".mp4",
        ExportPresetKind.Video,
        "yuv420p",
        [new EncoderTemplate("libx265", "-c:v libx265 -preset medium -crf {q} -pix_fmt yuv420p -tag:v hvc1")],
        [Aac192],
        ["-movflags", "+faststart"],
        [30, 26, 23, 20],
        []);

    /// <summary>VP9 in WebM: libvpx-vp9 constant quality; Opus 160k, falling back to Vorbis.</summary>
    public static ExportPreset Vp9 { get; } = new(
        "vp9",
        "VP9",
        ".webm",
        ExportPresetKind.Video,
        "yuv420p",
        [new EncoderTemplate("libvpx-vp9", "-c:v libvpx-vp9 -crf {q} -b:v 0 -row-mt 1 -deadline good -cpu-used 2 -pix_fmt yuv420p")],
        [
            new EncoderTemplate("libopus", "-c:a libopus -b:a 160k"),
            new EncoderTemplate("libvorbis", "-c:a libvorbis -q:a 5"),
        ],
        [],
        [40, 34, 31, 26],
        []);

    /// <summary>AV1 in MP4: SVT-AV1, falling back to libaom; AAC 192k.</summary>
    public static ExportPreset Av1 { get; } = new(
        "av1",
        "AV1",
        ".mp4",
        ExportPresetKind.Video,
        "yuv420p",
        [
            new EncoderTemplate("libsvtav1", "-c:v libsvtav1 -preset 8 -crf {q} -pix_fmt yuv420p"),
            new EncoderTemplate("libaom-av1", "-c:v libaom-av1 -cpu-used 6 -row-mt 1 -crf {q} -b:v 0 -pix_fmt yuv420p"),
        ],
        [Aac192],
        ["-movflags", "+faststart"],
        [42, 36, 32, 28],
        []);

    /// <summary>ProRes 422 HQ in MOV: prores_ks profile 3, 10-bit 4:2:2; PCM s16.</summary>
    public static ExportPreset ProRes { get; } = new(
        "prores",
        "ProRes 422 HQ",
        ".mov",
        ExportPresetKind.Video,
        "yuv422p10le",
        [new EncoderTemplate("prores_ks", "-c:v prores_ks -profile:v 3 -vendor apl0 -pix_fmt yuv422p10le")],
        [new EncoderTemplate("pcm_s16le", "-c:a pcm_s16le", AlwaysAvailable: true)],
        [],
        [],
        []);

    /// <summary>Animated GIF: 15 fps two-pass palette at a chosen width, no audio.</summary>
    public static ExportPreset Gif { get; } = new(
        "gif",
        "GIF",
        ".gif",
        ExportPresetKind.Gif,
        null,
        [new EncoderTemplate("gif", string.Empty, AlwaysAvailable: true)],
        [],
        ["-loop", "0"],
        [],
        ["palettegen", "paletteuse"]);

    /// <summary>MP3: LAME VBR.</summary>
    public static ExportPreset Mp3 { get; } = new(
        "mp3",
        "MP3",
        ".mp3",
        ExportPresetKind.Audio,
        null,
        [],
        [new EncoderTemplate("libmp3lame", "-vn -c:a libmp3lame -q:a {q}")],
        [],
        [6, 4, 2, 0],
        []);

    /// <summary>AAC 256k in M4A.</summary>
    public static ExportPreset Aac { get; } = new(
        "aac",
        "AAC",
        ".m4a",
        ExportPresetKind.Audio,
        null,
        [],
        [new EncoderTemplate("aac", "-vn -c:a aac -b:a 256k")],
        [],
        [],
        []);

    /// <summary>WAV: 16-bit PCM.</summary>
    public static ExportPreset Wav { get; } = new(
        "wav",
        "WAV",
        ".wav",
        ExportPresetKind.Audio,
        null,
        [],
        [new EncoderTemplate("pcm_s16le", "-vn -c:a pcm_s16le", AlwaysAvailable: true)],
        [],
        [],
        []);

    /// <summary>FLAC: lossless.</summary>
    public static ExportPreset Flac { get; } = new(
        "flac",
        "FLAC",
        ".flac",
        ExportPresetKind.Audio,
        null,
        [],
        [new EncoderTemplate("flac", "-vn -c:a flac", AlwaysAvailable: true)],
        [],
        [],
        []);

    /// <summary>Every preset in the order of the export dialog.</summary>
    public static ImmutableArray<ExportPreset> All { get; } = [H264, H265, Vp9, Av1, ProRes, Gif, Mp3, Aac, Wav, Flac];

    /// <summary>The default preset (H.264).</summary>
    public static ExportPreset Default => H264;

    /// <summary>The default quality.</summary>
    public static ExportQuality DefaultQuality => ExportQuality.High;

    /// <summary>The GIF widths the export dialog offers.</summary>
    public static ImmutableArray<int> GifWidths { get; } = [320, 480, 640, 800];

    /// <summary>The fixed output heights the export dialog offers besides Sequence and custom.</summary>
    public static ImmutableArray<int> Heights { get; } = [2160, 1080, 720];

    /// <summary>Finds a preset by id.</summary>
    /// <param name="id">The id, for example <c>h264</c> (ordinal, case-insensitive).</param>
    /// <returns>The preset, or null.</returns>
    public static ExportPreset? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The canvas for an output height with the sequence aspect locked; the width rounds to even.</summary>
    /// <param name="settings">The sequence settings.</param>
    /// <param name="height">The output height (rounded to even, at least 2).</param>
    /// <returns>The canvas size.</returns>
    public static (int Width, int Height) CanvasForHeight(SequenceSettings settings, int height)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var h = Even(height);
        return (EvenRatio((long)h * settings.Width, settings.Height), h);
    }

    /// <summary>The canvas for an output width with the sequence aspect locked (GIF); the height rounds to even.</summary>
    /// <param name="settings">The sequence settings.</param>
    /// <param name="width">The output width (rounded to even, at least 2).</param>
    /// <returns>The canvas size.</returns>
    public static (int Width, int Height) CanvasForWidth(SequenceSettings settings, int width)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var w = Even(width);
        return (w, EvenRatio((long)w * settings.Height, settings.Width));
    }

    private static int Even(int value) => Math.Max(2, value / 2 * 2);

    // The nearest even integer to numerator/denominator, halves rounding up, at least 2.
    private static int EvenRatio(long numerator, long denominator) =>
        (int)Math.Max(2, 2 * ((numerator + denominator) / (2 * denominator)));
}
