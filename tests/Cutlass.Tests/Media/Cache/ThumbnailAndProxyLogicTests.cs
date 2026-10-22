using Cutlass.Compiler;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.Media.Cache;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Media.Cache;

/// <summary>The pure parts of thumbnails, waveforms and proxies (TECH_BRIEF 6.5 and 6.6).</summary>
public sealed class ThumbnailAndProxyLogicTests
{
    [Theory]
    [InlineData(20_000_000, 1_000_000, 20)]
    [InlineData(20_500_000, 1_000_000, 21)]
    [InlineData(0, 1_000_000, 1)]
    [InlineData(40_000, 1_000_000, 1)]
    [InlineData(600_000_000, 1_000_000, 600)]
    [InlineData(3_600_000_000, 6_000_000, 600)]
    [InlineData(3_600_000_001, 6_000_000, 601)]
    public void IntervalIsAtLeastOneSecondAndCountCoversTheDuration(long durationMicros, long interval, int count)
    {
        Assert.Equal(interval, ThumbnailService.IntervalMicros(durationMicros));
        Assert.Equal(count, ThumbnailService.TileCount(durationMicros, interval));
    }

    [Fact]
    [UseCulture("de-DE")]
    public void ThumbnailArgumentsFollowTheBrief()
    {
        var args = ThumbnailService.BuildArguments("C:/m/it's.mp4", 0, 1_500_000, keyframesOnly: false, "C:/c/thumbs.tmp-1a2b3c4d/sheet_%03d.jpg");
        Assert.Equal(
            new[]
            {
                "-hide_banner", "-nostdin", "-v", "error", "-i", "C:/m/it's.mp4", "-map", "0:0", "-an", "-sn",
                "-vf", "fps=1/1.500000:round=up,scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2,tile=10x10",
                "-q:v", "5", "-progress", "pipe:1", "-nostats", "-y", "C:/c/thumbs.tmp-1a2b3c4d/sheet_%03d.jpg",
            },
            args);

        var longMedia = ThumbnailService.BuildArguments("in.mkv", 2, 2_000_000, keyframesOnly: true, "out_%03d.jpg");
        Assert.Equal(new[] { "-skip_frame", "nokey", "-i" }, longMedia.Skip(4).Take(3));
        Assert.Contains("0:2", longMedia);
    }

    [Fact]
    public void SheetGeometryPlacesTilesRowByRowAndClamps()
    {
        var sheet = new ThumbnailSheet(Path.Combine("c", "thumbs"), 2_000_000, 230);

        Assert.Equal(3, sheet.SheetCount);
        Assert.Equal(Path.Combine("c", "thumbs", "sheet_001.jpg"), sheet.SheetPath(0));
        Assert.Equal(Path.Combine("c", "thumbs", "sheet_003.jpg"), sheet.SheetPaths[2]);
        Assert.Equal(1600, ThumbnailSheet.SheetWidth);
        Assert.Equal(900, ThumbnailSheet.SheetHeight);

        Assert.Equal(0, sheet.IndexAt(-5));
        Assert.Equal(0, sheet.IndexAt(1_999_999));
        Assert.Equal(1, sheet.IndexAt(2_000_000));
        Assert.Equal(229, sheet.IndexAt(long.MaxValue));
        Assert.Equal(4_000_000, sheet.SourceMicrosOf(2));

        // Thumbnail 123: sheet 1, cell 23, so row 2 and column 3.
        var tile = sheet.Tile(123);
        Assert.Equal((sheet.SheetPath(1), 480, 180, 160, 90), tile);
        Assert.Equal(sheet.Tile(229), sheet.Tile(1000));
        Assert.Equal(sheet.Tile(123), sheet.TileAt(246_500_000));

        Assert.Equal(sheet.Tile(0), sheet.SkimTile(0));
        Assert.Equal(sheet.Tile(115), sheet.SkimTile(0.5));
        Assert.Equal(sheet.Tile(229), sheet.SkimTile(1));
        Assert.Equal(sheet.Tile(0), sheet.SkimTile(double.NaN));
    }

    [Fact]
    public void WaveformArgumentsFollowTheBrief()
    {
        Assert.Equal(
            new[] { "-hide_banner", "-nostdin", "-v", "error", "-i", "a b.wav", "-map", "0:1", "-vn", "-af", "aresample=async=1:first_pts=0", "-ac", "1", "-ar", "16000", "-f", "s16le", "pipe:1" },
            WaveformService.BuildArguments("a b.wav", 1));
    }

    [Fact]
    public void ProxyArgumentsFollowTheBrief()
    {
        Assert.Equal(
            new[]
            {
                "-hide_banner", "-nostdin", "-v", "error", "-y", "-i", "src.mp4", "-map", "0:0", "-an", "-sn", "-fps_mode", "passthrough",
                "-video_track_timescale", "12800", "-vf", "scale=w=-2:h=540:flags=bilinear,format=yuvj420p", "-c:v", "mjpeg", "-q:v", "4",
                "-progress", "pipe:1", "-nostats", "proxy540.tmp-1a2b3c4d.mov",
            },
            ProxyService.BuildArguments("src.mp4", 0, null, 12800, "proxy540.tmp-1a2b3c4d.mov"));

        var vfr = ProxyService.BuildArguments("src.mkv", 1, "demux", 1000, "out.mov");
        Assert.Equal(new[] { "-enc_time_base", "demux", "-video_track_timescale", "1000" }, vfr.Skip(13).Take(4));
    }

    [Theory]
    [InlineData(1920, 1080, "h264", false, 8_000_000, ProxyPolicy.Auto, false)]
    [InlineData(3840, 2160, "h264", false, 8_000_000, ProxyPolicy.Auto, true)]
    [InlineData(1920, 1080, "hevc", false, 8_000_000, ProxyPolicy.Auto, true)]
    [InlineData(1920, 1080, "av1", false, 8_000_000, ProxyPolicy.Auto, true)]
    [InlineData(1920, 1080, "vp9", false, 8_000_000, ProxyPolicy.Auto, true)]
    [InlineData(1280, 720, "h264", true, 8_000_000, ProxyPolicy.Auto, true)]
    [InlineData(1920, 1080, "h264", false, 60_000_000, ProxyPolicy.Auto, true)]
    [InlineData(640, 360, "h264", false, 1_000_000, ProxyPolicy.Always, true)]
    [InlineData(3840, 2160, "hevc", true, 90_000_000, ProxyPolicy.Never, false)]
    public void ProxyPolicyDecidesPerMedia(int w, int h, string codec, bool vfr, long bitrate, ProxyPolicy policy, bool expected)
    {
        var r = new Rational(25, 1);
        var video = new VideoStreamInfo(0, codec, w, h, w, h, 0, Rational.One, r, r, new Rational(1, 12800), 0, "yuv420p", vfr, false, bitrate);
        var asset = new MediaAsset("m_1", "x.mp4", null, "sha256:00", 1, DateTime.UnixEpoch, new MediaInfo(1_000_000, 0, "mp4", false, video, null));

        Assert.Equal(expected, ProxyService.ShouldBuild(asset, policy));
    }

    [Fact]
    public void StillsAndAudioOnlyMediaNeverGetAProxy()
    {
        var still = TestMedia.Asset("m_s", "a.png", 0, video: true, audio: false, still: true);
        var audio = TestMedia.Asset("m_a", "a.wav", 5_000_000, video: false, audio: true);

        Assert.False(ProxyService.ShouldBuild(still, ProxyPolicy.Always));
        Assert.False(ProxyService.ShouldBuild(audio, ProxyPolicy.Always));
    }

    [Fact]
    public void ProxyMappingIsExactRelativeToEachFileStart()
    {
        // Source in 1/12800 starting at 2.5 s; proxy in 1/12800 starting at 0.
        var source = new FrameIndex(new Rational(1, 12800), [32000, 32512, 33024]);
        var proxy = new FrameIndex(new Rational(1, 12800), [0, 512, 1024]);
        Assert.Equal(ProxyMapping.Exact, ProxyService.Classify(source, 2_500_000, proxy, 0));

        // Different time bases describing the same instants are still exact.
        var ms = new FrameIndex(new Rational(1, 1000), [0, 40, 80]);
        var tb12800 = new FrameIndex(new Rational(1, 12800), [0, 512, 1024]);
        Assert.Equal(ProxyMapping.Exact, ProxyService.Classify(ms, 0, tb12800, 0));

        // Same count, shifted timestamps: ordinal. Different count: rejected.
        Assert.Equal(ProxyMapping.Ordinal, ProxyService.Classify(source, 0, proxy, 0));
        Assert.Equal(ProxyMapping.Rejected, ProxyService.Classify(source, 2_500_000, new FrameIndex(proxy.TimeBase, [0, 512]), 0));

        // No proxy index: rejected. No source index (CFR fallback): ordinal.
        Assert.Equal(ProxyMapping.Rejected, ProxyService.Classify(source, 0, null, 0));
        Assert.Equal(ProxyMapping.Ordinal, ProxyService.Classify(null, 0, proxy, 0));
    }
}
