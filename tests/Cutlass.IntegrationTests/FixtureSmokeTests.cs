using System.Globalization;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests;

/// <summary>Proves real ffmpeg runs and the generated test media has the shape the later suites rely on.</summary>
[Collection(TestMediaCollection.Name)]
public sealed class FixtureSmokeTests
{
    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public FixtureSmokeTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public void EveryFixtureFileExists()
    {
        foreach (var file in _media.AllFiles)
        {
            Assert.True(File.Exists(file), file);
            Assert.True(new FileInfo(file).Length > 0, file);
        }

        Assert.Contains(_media.LongGopEncoder, new[] { "libx264", "mpeg4" });
    }

    [FfmpegFact]
    public async Task IntraOracleHasExactFrameCount()
    {
        var result = await TestProcess.RunAsync(
            TestFfmpeg.FfprobePath,
            ["-v", "error", "-count_frames", "-select_streams", "v:0", "-show_entries", "stream=nb_read_frames",
             "-of", "csv=p=0", _media.Oracle25Intra],
            _media.Directory,
            TimeSpan.FromMinutes(1));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(TestMediaFixture.Oracle25FrameCount, int.Parse(result.Stdout.Trim(), CultureInfo.InvariantCulture));
    }

    [FfmpegFact]
    public async Task OracleFramesDecodeToTheirIndex()
    {
        foreach (var frame in new[] { 0, 1, 37, 255, 256, 499 })
        {
            var result = await TestProcess.RunAsync(
                TestFfmpeg.FfmpegPath,
                ["-hide_banner", "-nostdin", "-v", "error", "-i", _media.Oracle25Intra,
                 "-vf", string.Create(CultureInfo.InvariantCulture, $"select=eq(n\\,{frame})"),
                 "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "gray", "pipe:1"],
                _media.Directory,
                TimeSpan.FromMinutes(1));

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(TestMediaFixture.OracleWidth * TestMediaFixture.OracleHeight, result.StdoutBytes.Length);
            Assert.Equal(
                TestMediaFixture.ExpectedOracleValue(frame),
                OracleReader.DecodeGray(result.StdoutBytes, TestMediaFixture.OracleWidth, TestMediaFixture.OracleHeight));
        }
    }

    [FfmpegFact]
    public async Task DelayedAudioStartsHalfASecondLate()
    {
        var result = await TestProcess.RunAsync(
            TestFfmpeg.FfprobePath,
            ["-v", "error", "-select_streams", "a:0", "-show_entries", "stream=start_time", "-of", "csv=p=0",
             _media.DelayedAudio],
            _media.Directory,
            TimeSpan.FromMinutes(1));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0.5, double.Parse(result.Stdout.Trim(), CultureInfo.InvariantCulture), 3);
    }
}
