using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests;

/// <summary>Checks the integration suite's own wiring without needing ffmpeg.</summary>
public sealed class EnvironmentSmokeTests
{
    [Fact]
    public void FfmpegFactSkipsExactlyWhenFfmpegIsMissing()
    {
        var fact = new FfmpegFactAttribute();

        Assert.Equal(TestFfmpeg.IsAvailable, fact.Skip is null);
    }

    [Fact]
    public void OracleReaderDecodesSyntheticFrames()
    {
        const int width = TestMediaFixture.OracleWidth;
        const int height = TestMediaFixture.OracleHeight;
        const int value = 0b1010_0000_0000_0101;
        var gray = new byte[width * height];
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var bit = (x / 40) + (8 * (y / 90));
                var on = (value & (1 << bit)) != 0;
                gray[(y * width) + x] = on ? (byte)235 : (byte)16;
                bgra[(((y * width) + x) * 4) + 1] = on ? (byte)255 : (byte)0;
            }
        }

        Assert.Equal(value, OracleReader.DecodeGray(gray, width, height));
        Assert.Equal(value, OracleReader.DecodeBgra(bgra, width, height));
    }
}
