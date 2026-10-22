using Cutlass.Core.Time;
using Cutlass.Media.Parsing;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Parsing;

/// <summary>ProbeParser on hand-written ffprobe JSON fixtures (TECH_BRIEF 6.3 and 11.1).</summary>
[UseCulture("de-DE")]
public sealed class ProbeParserTests
{
    [Fact]
    public void RotatedPhoneVideoUsesSideDataAndSwapsDisplaySize()
    {
        const string json = """
            {
              "streams": [
                { "index": 0, "codec_name": "hevc", "codec_type": "video", "width": 3840, "height": 2160,
                  "sample_aspect_ratio": "1:1", "pix_fmt": "yuv420p10le", "r_frame_rate": "30000/1001",
                  "avg_frame_rate": "30000/1001", "time_base": "1/600", "start_pts": 0, "bit_rate": "48000000",
                  "nb_frames": "1926", "color_transfer": "arib-std-b67",
                  "disposition": { "default": 1, "attached_pic": 0 },
                  "tags": { "rotate": "180" },
                  "side_data_list": [ { "side_data_type": "Display Matrix", "rotation": -90 } ] },
                { "index": 1, "codec_name": "aac", "codec_type": "audio", "sample_rate": "48000", "channels": 2,
                  "r_frame_rate": "0/0", "avg_frame_rate": "0/0", "time_base": "1/48000" }
              ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "start_time": "0.000000", "duration": "64.200000",
                          "bit_rate": "48500000" }
            }
            """;

        var info = ProbeParser.Parse(json);

        Assert.Equal(64_200_000, info.DurationMicros);
        Assert.Equal(0, info.FormatStartMicros);
        Assert.Equal("mov,mp4,m4a,3gp,3g2,mj2", info.FormatName);
        Assert.False(info.IsStill);
        var v = info.Video!;
        Assert.Equal(0, v.StreamIndex);
        Assert.Equal("hevc", v.Codec);
        Assert.Equal(90, v.Rotation); // side data wins over the tag; -90 counter-clockwise is 90 clockwise
        Assert.Equal(3840, v.Width);
        Assert.Equal(2160, v.Height);
        Assert.Equal(2160, v.DisplayWidth);
        Assert.Equal(3840, v.DisplayHeight);
        Assert.Equal(new Rational(30000, 1001), v.RFrameRate);
        Assert.Equal(new Rational(1, 600), v.TimeBase);
        Assert.Equal("yuv420p10le", v.PixFmt);
        Assert.True(v.IsHdr);
        Assert.False(v.IsVfr);
        Assert.Equal(48_000_000, v.BitRate);
        Assert.Equal(new Cutlass.Core.Model.AudioStreamInfo(1, "aac", 48_000, 2), info.Audio);
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("-90", 270)]
    [InlineData("270", 270)]
    [InlineData("180", 180)]
    [InlineData("0", 0)]
    public void LegacyRotateTagIsClockwise(string tag, int expected)
    {
        var json = $$"""
            { "streams": [ { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080,
                             "tags": { "rotate": "{{tag}}" } } ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "1.0" } }
            """;

        var v = ProbeParser.Parse(json).Video!;

        Assert.Equal(expected, v.Rotation);
        Assert.Equal(expected is 90 or 270 ? 1080 : 1920, v.DisplayWidth);
    }

    [Theory]
    [InlineData(90.0, true, 270)]
    [InlineData(-90.0, true, 90)]
    [InlineData(180.0, true, 180)]
    [InlineData(-180.0, true, 180)]
    [InlineData(89.9, true, 270)]
    [InlineData(450.0, false, 90)]
    [InlineData(double.NaN, false, 0)]
    public void RotationIsNormalised(double degrees, bool counterClockwise, int expected)
    {
        Assert.Equal(expected, ProbeParser.NormalizeRotation(degrees, counterClockwise));
    }

    [Fact]
    public void VfrAndStartTimeAndMissingStreamBitRate()
    {
        const string json = """
            { "streams": [ { "index": 0, "codec_name": "mpeg4", "codec_type": "video", "width": 320, "height": 180,
                             "sample_aspect_ratio": "0:1", "r_frame_rate": "30/1", "avg_frame_rate": "30000/2311",
                             "time_base": "1/1000", "start_pts": 2500 } ],
              "format": { "format_name": "matroska,webm", "start_time": "2.500000", "duration": "6.933000",
                          "bit_rate": "123456" } }
            """;

        var info = ProbeParser.Parse(json);

        Assert.Equal(2_500_000, info.FormatStartMicros);
        Assert.Equal(6_933_000, info.DurationMicros);
        Assert.True(info.Video!.IsVfr);
        Assert.Equal(Rational.One, info.Video.Sar);
        Assert.Equal(2500, info.Video.StartPts);
        Assert.Equal(123_456, info.Video.BitRate);
        Assert.Null(info.Audio);
    }

    [Fact]
    public void RatesWithinOnePercentAreNotVfr()
    {
        const string json = """
            { "streams": [ { "index": 0, "codec_name": "h264", "codec_type": "video", "width": 1280, "height": 720,
                             "r_frame_rate": "30/1", "avg_frame_rate": "2997/100" } ],
              "format": { "format_name": "mov", "duration": "1" } }
            """;

        Assert.False(ProbeParser.Parse(json).Video!.IsVfr);
    }

    [Fact]
    public void AnamorphicSarScalesDisplayWidthToAnEvenNumber()
    {
        const string json = """
            { "streams": [ { "index": 0, "codec_name": "mpeg2video", "codec_type": "video", "width": 720, "height": 576,
                             "sample_aspect_ratio": "64:45", "r_frame_rate": "25/1", "avg_frame_rate": "25/1" } ],
              "format": { "format_name": "mpegts", "start_time": "1.400000", "duration": "10.0" } }
            """;

        var info = ProbeParser.Parse(json);

        Assert.Equal(1024, info.Video!.DisplayWidth);
        Assert.Equal(576, info.Video.DisplayHeight);
        Assert.Equal(new Rational(64, 45), info.Video.Sar);
        Assert.Equal(1_400_000, info.FormatStartMicros);
    }

    [Theory]
    [InlineData(853, 1, 1, 854)]
    [InlineData(720, 8, 9, 640)]
    [InlineData(1, 1, 1, 2)]
    [InlineData(1440, 4, 3, 1920)]
    [InlineData(100, 101, 100, 102)]
    public void RoundEven(int width, long num, long den, int expected)
    {
        Assert.Equal(expected, ProbeParser.RoundEvenWidth(width, new Rational(num, den)));
    }

    [Fact]
    public void AudioOnlyFile()
    {
        const string json = """
            { "streams": [ { "index": 0, "codec_name": "mp3", "codec_type": "audio", "sample_rate": "44100",
                             "channels": 1, "duration": "3.5" } ],
              "format": { "format_name": "mp3", "start_time": "0.025057", "duration": "3.526531" } }
            """;

        var info = ProbeParser.Parse(json);

        Assert.Null(info.Video);
        Assert.False(info.IsStill);
        Assert.Equal(25_057, info.FormatStartMicros);
        Assert.Equal(3_526_531, info.DurationMicros);
        Assert.Equal(new Cutlass.Core.Model.AudioStreamInfo(0, "mp3", 44_100, 1), info.Audio);
    }

    [Fact]
    public void PngIsAStill()
    {
        const string json = """
            { "streams": [ { "index": 0, "codec_name": "png", "codec_type": "video", "width": 320, "height": 240,
                             "r_frame_rate": "25/1", "avg_frame_rate": "25/1", "time_base": "1/25" } ],
              "format": { "format_name": "png_pipe", "duration": "0.040000" } }
            """;

        var info = ProbeParser.Parse(json);

        Assert.True(info.IsStill);
        Assert.Equal(40_000, info.DurationMicros);
    }

    [Fact]
    public void MjpegVideoWithManyFramesIsNotAStill()
    {
        const string json = """
            { "streams": [ { "index": 0, "codec_name": "mjpeg", "codec_type": "video", "width": 640, "height": 480,
                             "nb_frames": "300" } ],
              "format": { "format_name": "avi", "duration": "10" } }
            """;

        Assert.False(ProbeParser.Parse(json).IsStill);
    }

    [Fact]
    public void AttachedPictureIsSkipped()
    {
        const string json = """
            { "streams": [
                { "index": 0, "codec_name": "mp3", "codec_type": "audio", "sample_rate": "44100", "channels": 2 },
                { "index": 1, "codec_name": "mjpeg", "codec_type": "video", "width": 500, "height": 500,
                  "disposition": { "attached_pic": 1 } } ],
              "format": { "format_name": "mp3", "duration": "180.0" } }
            """;

        var info = ProbeParser.Parse(json);

        Assert.Null(info.Video);
        Assert.False(info.IsStill);
        Assert.Equal(0, info.Audio!.StreamIndex);
    }

    [Fact]
    public void AttachedPictureBeforeTheRealVideoIsSkipped()
    {
        const string json = """
            { "streams": [
                { "index": 0, "codec_name": "png", "codec_type": "video", "width": 10, "height": 10,
                  "disposition": { "attached_pic": 1 } },
                { "index": 1, "codec_name": "h264", "codec_type": "video", "width": 1920, "height": 1080 } ],
              "format": { "format_name": "matroska,webm", "duration": "5" } }
            """;

        Assert.Equal(1, ProbeParser.Parse(json).Video!.StreamIndex);
    }

    [Fact]
    public void MissingFormatDurationFallsBackToTheStream()
    {
        const string json = """
            { "streams": [ { "index": 0, "codec_name": "vp9", "codec_type": "video", "width": 640, "height": 360,
                             "time_base": "1/1000", "duration_ts": 12345 },
                           { "index": 1, "codec_name": "opus", "codec_type": "audio", "sample_rate": "48000",
                             "channels": 2, "duration": "99.0" } ],
              "format": { "format_name": "matroska,webm" } }
            """;

        var info = ProbeParser.Parse(json);

        Assert.Equal(12_345_000, info.DurationMicros);
        Assert.Equal(0, info.FormatStartMicros);
    }

    [Fact]
    public void MissingEverythingStillParses()
    {
        var info = ProbeParser.Parse("{}");

        Assert.Equal(0, info.DurationMicros);
        Assert.Null(info.Video);
        Assert.Null(info.Audio);
        Assert.Equal(string.Empty, info.FormatName);
    }

    [Fact]
    public void InvalidJsonIsAFormatException()
    {
        Assert.Throws<FormatException>(() => ProbeParser.Parse("not json"));
        Assert.Throws<FormatException>(() => ProbeParser.Parse("[1, 2]"));
    }

    [Theory]
    [InlineData("1.5", 1_500_000L)]
    [InlineData("0.0000019", 1L)]
    [InlineData("-0.0000011", -2L)]
    [InlineData("N/A", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void MicrosAreFlooredInvariantly(string? text, long? expected)
    {
        Assert.Equal(expected, ProbeParser.ParseMicros(text));
    }

    [Theory]
    [InlineData("smpte2084", true)]
    [InlineData("arib-std-b67", true)]
    [InlineData("bt709", false)]
    [InlineData(null, false)]
    public void HdrFollowsTheTransferCharacteristic(string? transfer, bool expected)
    {
        var transferField = transfer is null ? string.Empty : $", \"color_transfer\": \"{transfer}\"";
        var json = "{ \"streams\": [ { \"index\": 0, \"codec_name\": \"hevc\", \"codec_type\": \"video\", "
            + "\"width\": 3840, \"height\": 2160, \"pix_fmt\": \"yuv420p10le\"" + transferField + " } ], "
            + "\"format\": { \"format_name\": \"matroska,webm\", \"duration\": \"2\" } }";

        Assert.Equal(expected, ProbeParser.Parse(json).Video!.IsHdr);
    }
}
