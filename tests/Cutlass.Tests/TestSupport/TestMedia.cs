using System.Collections.Immutable;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Tests.TestSupport;

/// <summary>
/// Media assets and clip factories for editing tests. Test sequences run at 25 fps, where one frame is exactly
/// 28,224,000 flicks, so source positions are easy to reason about.
/// </summary>
internal static class TestMedia
{
    /// <summary>100 s of video and audio (2500 frames at 25 fps), file name <c>long.mp4</c>.</summary>
    public const string Long = "m_long";

    /// <summary>4 s of video and audio (100 frames at 25 fps), file name <c>short.mov</c>.</summary>
    public const string Short = "m_short";

    /// <summary>A still image (unbounded), file name <c>still.png</c>.</summary>
    public const string Still = "m_still";

    /// <summary>60 s of audio only, file name <c>music.wav</c>.</summary>
    public const string Music = "m_music";

    /// <summary>10 s of 3840x2160 video at 30000/1001 with audio (for sequence adoption), file name <c>uhd.mp4</c>.</summary>
    public const string Uhd = "m_uhd";

    /// <summary>The test sequence rate.</summary>
    public static readonly FrameRate Rate = FrameRate.Fps25;

    /// <summary>Flicks per frame at the test rate.</summary>
    public static readonly long F = Rate.FlicksPerFrame;

    /// <summary>Every test asset.</summary>
    public static ImmutableArray<MediaAsset> All { get; } =
    [
        Asset(Long, "D:/Footage/long.mp4", 100_000_000, video: true, audio: true),
        Asset(Short, "D:/Footage/short.mov", 4_000_000, video: true, audio: true),
        Asset(Still, "D:/Footage/still.png", 40_000, video: true, audio: false, still: true),
        Asset(Music, "D:/Footage/music.wav", 60_000_000, video: false, audio: true),
        Asset(Uhd, "D:/Footage/uhd.mp4", 10_000_000, video: true, audio: true, width: 3840, height: 2160, rate: new Rational(30000, 1001)),
    ];

    /// <summary>Creates a media asset.</summary>
    public static MediaAsset Asset(
        string id,
        string path,
        long durationMicros,
        bool video,
        bool audio,
        bool still = false,
        int width = 1920,
        int height = 1080,
        Rational? rate = null)
    {
        var r = rate ?? new Rational(25, 1);
        var v = video
            ? new VideoStreamInfo(0, still ? "png" : "h264", width, height, width, height, 0, Rational.One, r, r, new Rational(1, 12800), 0, "yuv420p", false, false, 8_000_000)
            : null;
        var a = audio ? new AudioStreamInfo(video ? 1 : 0, "aac", 48_000, 2) : null;
        var info = new MediaInfo(still ? 0 : durationMicros, 0, still ? "png_pipe" : "mov,mp4,m4a,3gp,3g2,mj2", still, v, a);
        return new MediaAsset(id, path, null, "sha256:" + id, 1_000_000, new DateTime(2026, 10, 1, 9, 12, 44, DateTimeKind.Utc), info);
    }

    /// <summary>A video media clip; <paramref name="sourceFrame"/> is the source in point in 25 fps frames.</summary>
    public static MediaClip V(string id, long start, long duration, string media = Long, long sourceFrame = 0, string? link = null, int speed = 100) =>
        new(id, start, duration, true, link, media, StreamKind.Video, sourceFrame * F, speed, VideoProps.Default, AudioProps.Default);

    /// <summary>An audio media clip; <paramref name="sourceFrame"/> is the source in point in 25 fps frames.</summary>
    public static MediaClip A(string id, long start, long duration, string media = Long, long sourceFrame = 0, string? link = null, int speed = 100) =>
        new(id, start, duration, true, link, media, StreamKind.Audio, sourceFrame * F, speed, VideoProps.Default, AudioProps.Default);

    /// <summary>A title clip.</summary>
    public static TitleClip Title(string id, long start, long duration, string text = "Hello") =>
        new(id, start, duration, true, TitleSpec.Default with { Text = text }, 1, 0, 0);
}
