using System.Globalization;
using System.Text.Json;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Media.Parsing;

/// <summary>
/// Turns <c>ffprobe -v error -print_format json -show_format -show_streams</c> output into <see cref="MediaInfo"/>
/// (TECH_BRIEF 6.3). Every number is parsed with the invariant culture; ffprobe prints most of them as strings.
/// </summary>
public static class ProbeParser
{
    private static readonly string[] StillCodecs = ["png", "mjpeg", "webp", "bmp", "tiff"];

    /// <summary>Parses ffprobe JSON.</summary>
    /// <param name="json">The ffprobe stdout.</param>
    /// <returns>The media info. Video and audio are null when the file has no such stream.</returns>
    /// <exception cref="FormatException">When the text is not ffprobe JSON.</exception>
    public static MediaInfo Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Parse(doc.RootElement);
        }
        catch (JsonException ex)
        {
            throw new FormatException("ffprobe output is not valid JSON.", ex);
        }
    }

    /// <summary>Parses an already loaded ffprobe JSON root.</summary>
    /// <param name="root">The root object.</param>
    /// <returns>The media info.</returns>
    /// <exception cref="FormatException">When the root is not an object.</exception>
    public static MediaInfo Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("ffprobe output is not a JSON object.");
        }

        var format = root.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;
        var streams = root.TryGetProperty("streams", out var s) && s.ValueKind == JsonValueKind.Array
            ? s.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).ToList()
            : [];

        var formatName = GetString(format, "format_name") ?? string.Empty;
        var formatStart = ParseMicros(GetString(format, "start_time")) ?? 0;

        var videoElement = streams.FirstOrDefault(e => GetString(e, "codec_type") == "video" && !IsAttachedPicture(e));
        var audioElement = streams.FirstOrDefault(e => GetString(e, "codec_type") == "audio");
        var video = videoElement.ValueKind == JsonValueKind.Object ? ParseVideo(videoElement, format) : null;
        var audio = audioElement.ValueKind == JsonValueKind.Object ? ParseAudio(audioElement) : null;

        // Duration: format.duration, else the chosen streams' durations.
        var duration = ParseMicros(GetString(format, "duration"))
            ?? (videoElement.ValueKind == JsonValueKind.Object ? StreamDurationMicros(videoElement) : null)
            ?? (audioElement.ValueKind == JsonValueKind.Object ? StreamDurationMicros(audioElement) : null)
            ?? 0;

        var isStill = video is not null && IsStill(videoElement, video.Codec, formatName);
        return new MediaInfo(Math.Max(0, duration), formatStart, formatName, isStill, video, audio);
    }

    /// <summary>
    /// Normalises a rotation to clockwise degrees in {0, 90, 180, 270}. Display-matrix side data reports the
    /// counter-clockwise angle (a phone portrait clip reads <c>-90</c>), the legacy <c>rotate</c> tag the clockwise
    /// one (<c>90</c>), so side data is negated first.
    /// </summary>
    /// <param name="degrees">The raw angle.</param>
    /// <param name="counterClockwise">True for display-matrix side data.</param>
    /// <returns>The clockwise rotation, snapped to a multiple of 90.</returns>
    public static int NormalizeRotation(double degrees, bool counterClockwise)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees))
        {
            return 0;
        }

        var clockwise = counterClockwise ? -degrees : degrees;
        var quarter = (long)Math.Round(clockwise / 90.0, MidpointRounding.AwayFromZero);
        return (int)(((quarter % 4) + 4) % 4) * 90;
    }

    /// <summary>Rounds <c>width * sar</c> to the nearest even integer (halves round up), at least 2.</summary>
    /// <param name="width">The coded width.</param>
    /// <param name="sar">The sample aspect ratio (positive).</param>
    /// <returns>The display width.</returns>
    public static int RoundEvenWidth(int width, Rational sar)
    {
        // round(width * num / den / 2) * 2, in exact integer math.
        var twoDen = (Int128)2 * sar.Den;
        var half = (((Int128)width * sar.Num) + sar.Den) / twoDen;
        return (int)Math.Max(2, (long)half * 2);
    }

    /// <summary>Parses a decimal seconds string (<c>"12.345678"</c>) to microseconds, flooring.</summary>
    /// <param name="seconds">The text, or null.</param>
    /// <returns>The microseconds, or null when missing or <c>N/A</c>.</returns>
    public static long? ParseMicros(string? seconds)
    {
        if (string.IsNullOrWhiteSpace(seconds)
            || !decimal.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        try
        {
            return (long)decimal.Floor(value * 1_000_000m);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static VideoStreamInfo ParseVideo(JsonElement e, JsonElement format)
    {
        var width = (int)(GetLong(e, "width") ?? 0);
        var height = (int)(GetLong(e, "height") ?? 0);
        var sar = Rational.TryParse(GetString(e, "sample_aspect_ratio"), out var parsedSar) && parsedSar.IsPositive
            ? parsedSar
            : Rational.One; // "0:1" (unknown) and a missing SAR count as square pixels.
        var rotation = ReadRotation(e);

        var displayWidth = width > 0 ? RoundEvenWidth(width, sar) : 0;
        var displayHeight = height;
        if (rotation is 90 or 270)
        {
            (displayWidth, displayHeight) = (displayHeight, displayWidth);
        }

        var rFrameRate = ParseRate(GetString(e, "r_frame_rate"));
        var avgFrameRate = ParseRate(GetString(e, "avg_frame_rate"));

        // A missing or invalid time base falls back to microseconds, the unit ffprobe reports times in.
        var timeBase = Rational.TryParse(GetString(e, "time_base"), out var tb) && tb.IsPositive ? tb : new Rational(1, 1_000_000);
        var transfer = GetString(e, "color_transfer");

        // Many containers (mkv) report no per-stream bit rate; the container's rate is the best estimate then.
        var bitRate = GetLong(e, "bit_rate") ?? GetLong(format, "bit_rate") ?? 0;

        return new VideoStreamInfo(
            StreamIndex: (int)(GetLong(e, "index") ?? 0),
            Codec: GetString(e, "codec_name") ?? string.Empty,
            Width: width,
            Height: height,
            DisplayWidth: displayWidth,
            DisplayHeight: displayHeight,
            Rotation: rotation,
            Sar: sar,
            RFrameRate: rFrameRate,
            AvgFrameRate: avgFrameRate,
            TimeBase: timeBase,
            StartPts: GetLong(e, "start_pts") ?? 0,
            PixFmt: GetString(e, "pix_fmt") ?? string.Empty,
            IsVfr: IsVfr(rFrameRate, avgFrameRate),
            IsHdr: transfer is "smpte2084" or "arib-std-b67",
            BitRate: Math.Max(0, bitRate));
    }

    private static AudioStreamInfo ParseAudio(JsonElement e) => new(
        StreamIndex: (int)(GetLong(e, "index") ?? 0),
        Codec: GetString(e, "codec_name") ?? string.Empty,
        SampleRate: (int)(GetLong(e, "sample_rate") ?? 0),
        Channels: (int)(GetLong(e, "channels") ?? 0));

    private static int ReadRotation(JsonElement e)
    {
        if (e.TryGetProperty("side_data_list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && GetDouble(item, "rotation") is { } sideRotation)
                {
                    return NormalizeRotation(sideRotation, counterClockwise: true);
                }
            }
        }

        if (e.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object
            && GetDouble(tags, "rotate") is { } tagRotation)
        {
            return NormalizeRotation(tagRotation, counterClockwise: false);
        }

        return 0;
    }

    private static bool IsVfr(Rational r, Rational avg)
    {
        // Unknown rates (0/0) cannot show variability.
        if (!r.IsPositive || !avg.IsPositive)
        {
            return false;
        }

        var rd = r.ToDouble();
        return Math.Abs(rd - avg.ToDouble()) > 0.01 * rd;
    }

    private static bool IsStill(JsonElement video, string codec, string formatName)
    {
        if (!StillCodecs.Contains(codec, StringComparer.Ordinal))
        {
            return false;
        }

        var frames = GetLong(video, "nb_frames");
        return frames is <= 1
            || formatName.Contains("image2", StringComparison.Ordinal)
            || formatName.Contains("_pipe", StringComparison.Ordinal);
    }

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d) && d.ValueKind == JsonValueKind.Object
        && GetLong(d, "attached_pic") == 1;

    private static long? StreamDurationMicros(JsonElement stream)
    {
        if (ParseMicros(GetString(stream, "duration")) is { } micros)
        {
            return micros;
        }

        if (GetLong(stream, "duration_ts") is { } ticks
            && Rational.TryParse(GetString(stream, "time_base"), out var tb) && tb.IsPositive)
        {
            return (long)TimeMath.FloorDiv((Int128)ticks * tb.Num * TimeMath.MicrosPerSecond, tb.Den);
        }

        return null;
    }

    private static Rational ParseRate(string? text) =>
        Rational.TryParse(text, out var rate) && rate.IsPositive ? rate : Rational.Zero;

    private static string? GetString(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static long? GetLong(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? GetDouble(JsonElement obj, string name)
    {
        var text = GetString(obj, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
