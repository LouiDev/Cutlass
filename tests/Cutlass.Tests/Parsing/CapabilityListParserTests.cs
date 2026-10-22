using Cutlass.Media.Parsing;

namespace Cutlass.Tests.Parsing;

/// <summary>CapabilityListParser on <c>-filters</c> and <c>-encoders</c> listings from master and 6.1.</summary>
public sealed class CapabilityListParserTests
{
    private const string MasterFilters =
        "Filters:\r\n" +
        "  T.. = Timeline support\r\n" +
        "  .S. = Slice threading\r\n" +
        "  A = Audio input/output\r\n" +
        "  V = Video input/output\r\n" +
        "  N = Dynamic number and/or type of input/output\r\n" +
        "  | = Source or sink filter\r\n" +
        "  ------\r\n" +
        " TS aap               AA->A      Apply Affine Projection algorithm to first audio stream.\r\n" +
        " .. acrossfade        AA->A      Cross fade two input audio streams.\r\n" +
        " .. anullsrc          |->A       Null audio source, return empty audio frames.\r\n" +
        " .. concat            N->N       Concatenate audio and video streams.\r\n" +
        " T. drawtext          V->V       Draw text on top of video frames using libfreetype library.\r\n" +
        " .S xfade             VV->V      Cross fade one video with another video.\r\n" +
        " .. nullsink          V->|       Do absolutely nothing with the input video.\r\n";

    private const string Ubuntu61Filters =
        "Filters:\n" +
        "  T.. = Timeline support\n" +
        "  .S. = Slice threading\n" +
        "  ..C = Command support\n" +
        "  A = Audio input/output\n" +
        "  V = Video input/output\n" +
        "  N = Dynamic number and/or type of input/output\n" +
        "  | = Source or sink filter\n" +
        " ... abench            A->A       Benchmark part of a filtergraph.\n" +
        " ..C acompressor       A->A       Audio compressor.\n" +
        " TSC overlay           VV->V      Overlay a video source on top of the input.\n" +
        " ... color             |->V       Provide an uniformly colored input.\n";

    private const string MasterEncoders =
        "Encoders:\r\n" +
        " V..... = Video\r\n" +
        " A..... = Audio\r\n" +
        " S..... = Subtitle\r\n" +
        " .F.... = Frame-level multithreading\r\n" +
        " ..S... = Slice-level multithreading\r\n" +
        " ...X.. = Codec is experimental\r\n" +
        " ....B. = Supports draw_horiz_band\r\n" +
        " .....D = Supports direct rendering method 1\r\n" +
        " ------\r\n" +
        " V....D libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)\r\n" +
        " VFS..D ffv1                 FFmpeg video codec #1\r\n" +
        " A....D aac                  AAC (Advanced Audio Coding)\r\n" +
        " A....D pcm_s16le            PCM signed 16-bit little-endian\r\n" +
        " S..... srt                  SubRip subtitle\r\n";

    [Fact]
    public void MasterFiltersUseTwoFlagColumns()
    {
        Assert.Equal(
            new[] { "aap", "acrossfade", "anullsrc", "concat", "drawtext", "xfade", "nullsink" },
            CapabilityListParser.ParseFilters(MasterFilters));
    }

    [Fact]
    public void Ubuntu61FiltersUseThreeFlagColumns()
    {
        Assert.Equal(
            new[] { "abench", "acompressor", "overlay", "color" },
            CapabilityListParser.ParseFilters(Ubuntu61Filters));
    }

    [Fact]
    public void EncodersSkipTheLegend()
    {
        Assert.Equal(
            new[] { "libx264", "ffv1", "aac", "pcm_s16le", "srt" },
            CapabilityListParser.ParseEncoders(MasterEncoders));
    }

    [Fact]
    public void EmptyInputGivesEmptyLists()
    {
        Assert.Empty(CapabilityListParser.ParseFilters(null));
        Assert.Empty(CapabilityListParser.ParseEncoders(string.Empty));
        Assert.Empty(CapabilityListParser.ParseEncoders(" V....D libx264  no separator yet\n"));
    }
}
