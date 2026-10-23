using System.Globalization;
using System.Text.Json;
using Cutlass.Core.Time;

namespace Cutlass.App.Services.Smoke;

/// <summary>
/// The M7 <c>probe</c> checks for every export preset (TECH_BRIEF 12, "every preset"): the container, missing streams,
/// the pixel format, the codec tag (<c>hvc1</c>) and the duration of audio-only files.
/// </summary>
public sealed partial class SmokeRunner
{
    /// <summary>The default duration tolerance of <c>probe duration=</c> in seconds (encoder padding of MP3 and AAC).</summary>
    public const double DefaultDurationTolerance = 0.05;

    // container=<name> [novideo] [noaudio] [pixfmt=<fmt>] [tag=<fourcc>] [duration=<s>|export [tolerance=<s>]]
    private void ExpectProbeM7(SmokeStep step, JsonElement root, JsonElement video, JsonElement audio)
    {
        var hasVideo = video.ValueKind == JsonValueKind.Object;
        var hasAudio = audio.ValueKind == JsonValueKind.Object;
        var format = root.TryGetProperty("format", out var f) ? f : default;
        if (step.Option("container") is { } container)
        {
            var names = (Text(format, "format_name") ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            Expect(names.Contains(container, StringComparer.OrdinalIgnoreCase), "the container is " + Text(format, "format_name") + ", expected " + container);
            _log.Write("probe container: " + Text(format, "format_name"));
        }

        if (step.Flag("novideo"))
        {
            Expect(!hasVideo, "the file has a video stream (" + Text(video, "codec_name") + ")");
        }

        if (step.Flag("noaudio"))
        {
            Expect(!hasAudio, "the file has an audio stream (" + Text(audio, "codec_name") + ")");
        }

        if (step.Option("pixfmt") is { } pixfmt)
        {
            Expect(hasVideo && Text(video, "pix_fmt") == pixfmt, "the pixel format is " + Text(video, "pix_fmt") + ", expected " + pixfmt);
        }

        if (step.Option("tag") is { } tag)
        {
            Expect(hasVideo && Text(video, "codec_tag_string") == tag, "the video codec tag is " + Text(video, "codec_tag_string") + ", expected " + tag);
        }

        if (step.Option("duration") is { } durationText)
        {
            double expected;
            if (string.Equals(durationText, "export", StringComparison.OrdinalIgnoreCase))
            {
                Expect(_lastExportFrames >= 0, "duration=export needs an export first");
                expected = TimeMath.SecondsAt(_lastExportFrames, _vm.Session.Current.Sequence.Settings.Rate).ToDouble();
            }
            else
            {
                expected = double.Parse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            var tolerance = step.Option("tolerance") is { } t ? double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture) : DefaultDurationTolerance;
            var streamText = hasAudio ? Text(audio, "duration") : null;
            var text = streamText is not null && streamText != "N/A" ? streamText : Text(format, "duration");
            Expect(
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual),
                "ffprobe reported no duration");
            Expect(
                Math.Abs(actual - expected) <= tolerance,
                string.Create(CultureInfo.InvariantCulture, $"the duration is {actual:0.000} s, expected {expected:0.000} s within {tolerance:0.000} s"));
            _log.Write(string.Create(CultureInfo.InvariantCulture, $"probe duration {actual:0.000} s (expected {expected:0.000} s)"));
        }
    }
}
