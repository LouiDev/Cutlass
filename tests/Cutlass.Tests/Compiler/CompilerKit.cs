using System.Collections.Immutable;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Tests.Compiler;

/// <summary>Builders for compiler test projects: assets, clips, tracks and requests.</summary>
internal static class CompilerKit
{
    /// <summary>Flicks per second.</summary>
    public const long F = TimeMath.FlicksPerSecond;

    /// <summary>A video asset (with audio on stream 1 unless <paramref name="audio"/> is false).</summary>
    public static MediaAsset VideoAsset(
        string id,
        string path,
        int width = 1920,
        int height = 1080,
        Rational? rate = null,
        long durationMicros = 60_000_000,
        bool audio = true,
        long formatStartMicros = 0,
        long startPts = 0,
        Rational? timeBase = null,
        int displayWidth = 0,
        int displayHeight = 0)
    {
        var r = rate ?? new Rational(30, 1);
        var tb = timeBase ?? new Rational(1, 15360);
        var v = new VideoStreamInfo(
            0, "h264", width, height, displayWidth > 0 ? displayWidth : width, displayHeight > 0 ? displayHeight : height, 0,
            Rational.One, r, r, tb, startPts, "yuv420p", false, false, 8_000_000);
        var a = audio ? new AudioStreamInfo(1, "aac", 48_000, 2) : null;
        var info = new MediaInfo(durationMicros, formatStartMicros, "mov,mp4,m4a,3gp,3g2,mj2", false, v, a);
        return new MediaAsset(id, path, null, "sha256:" + id, 1_000_000, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), info);
    }

    /// <summary>An audio-only asset.</summary>
    public static MediaAsset AudioAsset(string id, string path, long durationMicros = 60_000_000)
    {
        var info = new MediaInfo(durationMicros, 0, "wav", false, null, new AudioStreamInfo(0, "pcm_s16le", 48_000, 2));
        return new MediaAsset(id, path, null, "sha256:" + id, 1_000_000, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), info);
    }

    /// <summary>A still image asset.</summary>
    public static MediaAsset StillAsset(string id, string path, int width, int height)
    {
        var v = new VideoStreamInfo(
            0, "png", width, height, width, height, 0, Rational.One, new Rational(25, 1), new Rational(25, 1), new Rational(1, 25), 0, "rgb24",
            false, false, 0);
        var info = new MediaInfo(0, 0, "png_pipe", true, v, null);
        return new MediaAsset(id, path, null, "sha256:" + id, 1_000, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), info);
    }

    /// <summary>A video clip.</summary>
    public static MediaClip V(
        string id,
        long start,
        long duration,
        string media,
        long sourceInFlicks = 0,
        int speed = 100,
        VideoProps? video = null,
        string? link = null,
        bool enabled = true) =>
        new(id, start, duration, enabled, link, media, StreamKind.Video, sourceInFlicks, speed, video ?? VideoProps.Default, AudioProps.Default);

    /// <summary>An audio clip.</summary>
    public static MediaClip A(
        string id,
        long start,
        long duration,
        string media,
        long sourceInFlicks = 0,
        int speed = 100,
        AudioProps? audio = null,
        string? link = null) =>
        new(id, start, duration, true, link, media, StreamKind.Audio, sourceInFlicks, speed, VideoProps.Default, audio ?? AudioProps.Default);

    /// <summary>A title clip.</summary>
    public static TitleClip Title(string id, long start, long duration, TitleSpec? spec = null, double opacity = 1, long fadeIn = 0, long fadeOut = 0) =>
        new(id, start, duration, true, spec ?? TitleSpec.Default, opacity, fadeIn, fadeOut);

    /// <summary>A video track.</summary>
    public static Track VideoTrack(string id, IEnumerable<Clip> clips, IEnumerable<Transition>? transitions = null, bool hidden = false) =>
        new(id, TrackKind.Video, id.ToUpperInvariant(), hidden, false, false, false, false, 0,
            [.. clips.OrderBy(c => c.Start)], [.. transitions ?? []]);

    /// <summary>An audio track.</summary>
    public static Track AudioTrack(
        string id,
        IEnumerable<Clip> clips,
        IEnumerable<Transition>? transitions = null,
        bool muted = false,
        bool solo = false,
        double gainDb = 0) =>
        new(id, TrackKind.Audio, id.ToUpperInvariant(), false, false, false, muted, solo, gainDb,
            [.. clips.OrderBy(c => c.Start)], [.. transitions ?? []]);

    /// <summary>A project.</summary>
    public static Project Project(SequenceSettings settings, IEnumerable<MediaAsset> media, params Track[] tracks) =>
        new(Cutlass.Core.Model.Project.CurrentSchemaVersion, [.. media], new Sequence(settings, [.. tracks], ImmutableArray<Marker>.Empty, null, null, true));

    /// <summary>An export request with video and mixed audio.</summary>
    public static RenderRequest Export(long start, long end, int width = 1920, int height = 1080, bool video = true, bool audio = true, bool limit = true) =>
        new(start, end, width, height, RenderPurpose.Export, video, audio, false, AudioLayout.Mixed, 1, OutputTail.ForExport("yuv420p"), limit);

    /// <summary>A one-frame or window preview request (video only).</summary>
    public static RenderRequest PreviewFrame(long start, long end, int width, int height) =>
        new(start, end, width, height, RenderPurpose.PreviewFrame, true, false, false, AudioLayout.Mixed, 1, OutputTail.Preview, false);

    /// <summary>The worked example of TECH_BRIEF 7.8.</summary>
    public static Project WorkedExample()
    {
        var settings = new SequenceSettings(1920, 1080, FrameRate.Fps30);
        MediaAsset[] media =
        [
            VideoAsset("m_a", "D:/Clips/a.mp4"),
            VideoAsset("m_b", "D:/Clips/b.mp4"),
            VideoAsset("m_c", "D:/Clips/c.mp4"),
        ];
        var saturated = VideoProps.Default with { Color = ColorAdjust.Neutral with { Saturation = 1.2 } };
        var pip = VideoProps.Default with { Transform = Transform.Identity with { X = 480, Y = -270, Scale = 0.4 } };
        var keys = AudioProps.Default with { Keys = [new VolumeKey(0, -20), new VolumeKey(30, 0)] };
        var v1 = VideoTrack(
            "tr_v1",
            [V("c_a", 0, 150, "m_a", 2 * F, link: "l_a"), V("c_b", 150, 120, "m_b", 10 * F, video: saturated, link: "l_b")],
            [new Transition("x_1", TransitionKind.Crossfade, "c_a", "c_b", 30)]);
        var v2 = VideoTrack(
            "tr_v2",
            [Title("c_t", 30, 90, TitleSpec.Default with { Text = "Day One" }, fadeIn: 15, fadeOut: 15), V("c_c", 180, 90, "m_c", 0, video: pip, link: "l_c")]);
        var a1 = AudioTrack(
            "tr_a1",
            [A("c_aa", 0, 150, "m_a", 2 * F, link: "l_a"), A("c_ba", 150, 120, "m_b", 10 * F, link: "l_b")],
            [new Transition("x_2", TransitionKind.AudioCrossfade, "c_aa", "c_ba", 30)]);
        var a2 = AudioTrack("tr_a2", [A("c_ca", 180, 90, "m_c", 0, audio: keys, link: "l_c")]);
        return Project(settings, media, v1, v2, a1, a2);
    }
}
