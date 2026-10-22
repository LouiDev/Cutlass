using Cutlass.Media.Parsing;

namespace Cutlass.Tests.Parsing;

/// <summary>VersionParser on real <c>-version</c> output from a git build and a distribution 6.1 build.</summary>
public sealed class VersionParserTests
{
    private const string MasterOutput =
        "ffmpeg version N-126308-gd411d9e752-20260828 Copyright (c) 2000-2026 the FFmpeg developers\r\n" +
        "built with gcc 15.2.0 (crosstool-NG 1.28.0.23_185f348)\r\n" +
        "configuration: --prefix=/ffbuild/prefix --enable-gpl --enable-version3\r\n" +
        "libavutil      61.  6.100 / 61.  6.100\r\n" +
        "libavcodec     63.  8.101 / 63.  8.101\r\n" +
        "libavformat    63.  6.100 / 63.  6.100\r\n" +
        "libavdevice    63.  2.100 / 63.  2.100\r\n" +
        "libavfilter    12.  3.101 / 12.  3.101\r\n" +
        "libswscale     10.  2.100 / 10.  2.100\r\n" +
        "libswresample   7.  2.100 /  7.  2.100\r\n" +
        "\r\n" +
        "Exiting with exit code 0\r\n";

    private const string Ubuntu61Output =
        "ffmpeg version 6.1.1-3ubuntu5 Copyright (c) 2000-2023 the FFmpeg developers\n" +
        "built with gcc 13 (Ubuntu 13.2.0-23ubuntu3)\n" +
        "configuration: --prefix=/usr --extra-version=3ubuntu5 --toolchain=hardened\n" +
        "libavutil      58. 29.100 / 58. 29.100\n" +
        "libavcodec     60. 31.102 / 60. 31.102\n" +
        "libavformat    60. 16.100 / 60. 16.100\n" +
        "libavdevice    60.  3.100 / 60.  3.100\n" +
        "libavfilter     9. 12.100 /  9. 12.100\n" +
        "libswscale      7.  5.100 /  7.  5.100\n" +
        "libswresample   4. 12.100 /  4. 12.100\n" +
        "libpostproc    57.  3.100 / 57.  3.100\n";

    [Fact]
    public void GitBuild()
    {
        var version = VersionParser.Parse(MasterOutput);

        Assert.Equal(new FfmpegVersion("N-126308-gd411d9e752-20260828", 12, 3), version);
        Assert.Equal("N-126308, libavfilter 12", version!.DisplayText);
    }

    [Fact]
    public void Ubuntu61Build()
    {
        var version = VersionParser.Parse(Ubuntu61Output);

        Assert.Equal(new FfmpegVersion("6.1.1-3ubuntu5", 9, 12), version);
        Assert.Equal("6.1.1-3ubuntu5, libavfilter 9", version!.DisplayText);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ffprobe version N-126308 Copyright\nlibavfilter    12.  3.101 / 12.  3.101\n")]
    [InlineData("ffmpeg version N-1 Copyright\nlibavutil 61. 6.100\n")]
    public void UnusableOutputGivesNull(string? text)
    {
        Assert.Null(VersionParser.Parse(text));
    }
}
