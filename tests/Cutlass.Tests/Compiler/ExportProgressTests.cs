using Cutlass.Media.Export;
using Cutlass.Media.Parsing;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>
/// Export progress (TECH_BRIEF 9): ffmpeg's <c>-progress</c> blocks become fraction, speed and ETA, through partial
/// reads, <c>N/A</c> values and the final <c>progress=end</c>.
/// </summary>
[UseCulture("de-DE")]
public sealed class ExportProgressTests
{
    private const long TotalMicros = 9_000_000;

    [Fact]
    public void ContinueBlocksGiveFractionSpeedAndEta()
    {
        var progress = ExportProgress.FromBlock(new ProgressBlock(3_000_000, 90, 2.0, IsEnd: false), TotalMicros, null);

        Assert.Equal(ExportJobState.Running, progress.State);
        Assert.Equal(1.0 / 3.0, progress.Fraction, 6);
        Assert.Equal(2.0, progress.Speed);
        Assert.Equal(TimeSpan.FromSeconds(3), progress.Eta);
        Assert.Equal(3_000_000, progress.OutTimeMicros);
    }

    [Fact]
    public void EndBlockIsComplete()
    {
        var progress = ExportProgress.FromBlock(new ProgressBlock(8_966_667, 269, 5.7, IsEnd: true), TotalMicros, null);

        Assert.Equal(1.0, progress.Fraction);
        Assert.Equal(TimeSpan.FromSeconds(0), progress.Eta);
    }

    [Fact]
    public void MissingValuesKeepThePreviousPositionAndHaveNoEta()
    {
        var previous = ExportProgress.FromBlock(new ProgressBlock(4_500_000, 135, 1.5, IsEnd: false), TotalMicros, null);

        var next = ExportProgress.FromBlock(new ProgressBlock(null, null, null, IsEnd: false), TotalMicros, previous);

        Assert.Equal(4_500_000, next.OutTimeMicros);
        Assert.Equal(0.5, next.Fraction, 6);
        Assert.Null(next.Speed);
        Assert.Null(next.Eta);
    }

    [Fact]
    public void PositionsAreClampedToTheWindow()
    {
        Assert.Equal(1.0, ExportProgress.FromBlock(new ProgressBlock(12_000_000, null, 1, false), TotalMicros, null).Fraction);
        Assert.Equal(0.0, ExportProgress.FromBlock(new ProgressBlock(-50, null, 1, false), TotalMicros, null).Fraction);
        Assert.Null(ExportProgress.FromBlock(new ProgressBlock(1, null, 0, false), TotalMicros, null).Speed);
    }

    [Fact]
    public void ParserFeedsBlocksAcrossPartialLines()
    {
        // A real ffmpeg -progress stream (CRLF on Windows), split at awkward places, ending with progress=end.
        const string stream =
            "frame=45\r\nfps=0.00\r\nstream_0_0_q=-0.0\r\nbitrate=N/A\r\ntotal_size=48\r\nout_time_us=1500000\r\n" +
            "out_time_ms=1500000\r\nout_time=00:00:01.500000\r\ndup_frames=0\r\ndrop_frames=0\r\nspeed=2.99x\r\nprogress=continue\r\n" +
            "frame=270\r\nout_time_us=N/A\r\nout_time_ms=9000000\r\nspeed= 3.1x\r\nprogress=end\r\n";
        var parser = new ProgressParser();
        var reports = new List<ExportProgress>();
        ExportProgress? previous = null;

        foreach (var chunk in new[] { stream[..17], stream[17..60], stream[60..203], stream[203..204], stream[204..] })
        {
            foreach (var block in parser.Feed(chunk))
            {
                previous = ExportProgress.FromBlock(block, TotalMicros, previous);
                reports.Add(previous);
            }
        }

        Assert.Equal(2, reports.Count);
        Assert.Equal(1.0 / 6.0, reports[0].Fraction, 6);
        Assert.Equal(2.99, reports[0].Speed);
        Assert.Equal(9_000_000, reports[1].OutTimeMicros);
        Assert.Equal(3.1, reports[1].Speed);
        Assert.Equal(1.0, reports[1].Fraction);
        Assert.True(parser.Last!.IsEnd);
    }

    [Fact]
    public void PartialPathSitsNextToTheOutput()
    {
        var request = new ExportRequest(
            CompilerKit.WorkedExample(),
            new TestCatalog(),
            Cutlass.Compiler.Export.ExportPresets.H264,
            Cutlass.Compiler.Export.ExportQuality.High,
            1920,
            1080,
            0,
            270,
            true,
            Path.Combine(Path.GetTempPath(), "Out", "final.mp4"));
        var noExtension = request with { OutputPath = Path.Combine(Path.GetTempPath(), "Out", "final") };

        Assert.Equal(Path.Combine(Path.GetTempPath(), "Out", "final.partial.mp4"), request.PartialPath);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Out", "final.mp4"), noExtension.FinalPath);
        Assert.Equal((0L, 270L), ExportRequest.WholeRange(request.Project));
        Assert.Equal((0L, 270L), ExportRequest.InOutRange(request.Project));
        var render = request.ToRenderRequest();
        Assert.Equal(Cutlass.Compiler.RenderPurpose.Export, render.Purpose);
        Assert.True(render.IncludeVideo && render.IncludeAudio && render.LimitPeaks && !render.UseProxies);
        Assert.Equal("yuv420p", render.Tail.PixelFormat);
    }
}
