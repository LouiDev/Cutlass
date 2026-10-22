using Cutlass.Compiler.Emit;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Compiler;

/// <summary>
/// Golden tests (TECH_BRIEF 11.1): fixed timelines compile to the scripts and inputs stored under <c>Goldens/</c>,
/// byte for byte, under de-DE so any culture leak shows up. <c>CUTLASS_UPDATE_GOLDEN=1</c> rewrites them.
/// </summary>
[UseCulture("de-DE")]
public sealed class CompilerGoldenTests
{
    /// <summary>The case names.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var c in GoldenCases.All)
        {
            data.Add(c.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void CompilesToTheGoldenScript(string name)
    {
        var c = GoldenCases.Get(name);
        ModelAssert.Valid(c.Project);

        var render = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);

        Golden.Verify(name, render);
        Assert.DoesNotContain(",,", render.GraphText, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", render.GraphText, StringComparison.Ordinal);
        Assert.False(render.GraphText.EndsWith('\n'));
    }

    [Fact]
    public void CountsMatchTheWindow()
    {
        var gif = TimelineCompiler.Compile(GoldenCases.Get("gif").Project, GoldenCases.Get("gif").Catalog, GoldenCases.Get("gif").Request);
        Assert.Equal(15, gif.FrameCount);
        Assert.Null(gif.AudioLabel);
        Assert.Equal(0, gif.AudioChannels);

        var step = GoldenCases.Get("framestep");
        var stepped = TimelineCompiler.Compile(step.Project, step.Catalog, step.Request);
        Assert.Equal(45, stepped.FrameCount);

        var perTrack = GoldenCases.Get("per_track_audio");
        var pcm = TimelineCompiler.Compile(perTrack.Project, perTrack.Catalog, perTrack.Request);
        Assert.Equal(6, pcm.AudioChannels);
        Assert.Equal(48_000, pcm.SampleCount);
        Assert.Null(pcm.VideoLabel);
        Assert.Equal(0, pcm.FrameCount);

        var audioOnly = GoldenCases.Get("audio_only");
        var mp3 = TimelineCompiler.Compile(audioOnly.Project, audioOnly.Catalog, audioOnly.Request);
        Assert.Equal(96_000, mp3.SampleCount);
        Assert.Equal(2, mp3.AudioChannels);
        Assert.Null(mp3.VideoLabel);
    }

    [Fact]
    public void OfflineMediaRendersAsAGapWithAWarning()
    {
        var c = GoldenCases.Get("offline");

        var render = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);

        Assert.Contains(render.Warnings, w => w.Contains("m_b", StringComparison.Ordinal) && w.Contains("gap", StringComparison.Ordinal));
        Assert.Contains(render.Warnings, w => w.Contains("m_b", StringComparison.Ordinal) && w.Contains("silent", StringComparison.Ordinal));
        Assert.Single(render.Inputs);
        Assert.Equal(60, render.FrameCount);
    }

    [Fact]
    public void WindowOutsideATransitionStillTakesTheRunPixelPath()
    {
        var c = GoldenCases.Get("window_outside_transition");

        var render = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);

        // c_1 is transformed and alone in the window, but its run has three clips: canvas step, then yuva444p.
        Assert.Contains("[cv0s0]overlay=", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("format=yuva444p,setpts=PTS-STARTPTS+0[v0r0]", render.GraphText, StringComparison.Ordinal);
        Assert.Contains("[base][v0r0]overlay=x=0:y=0", render.GraphText, StringComparison.Ordinal);
    }

    [Fact]
    public void ProxiesTakeTheOrdinalFromTheOriginalAndThePtsFromTheProxy()
    {
        var c = GoldenCases.Get("proxy");

        var render = TimelineCompiler.Compile(c.Project, c.Catalog, c.Request);

        var input = Assert.Single(render.Inputs);
        Assert.Equal("C:/Cache/v1/media/ab/abcd/proxy540.mov", input.Path);

        // Ordinal 60 (2.0 s in 1/30000) has pts 60007 in the proxy: 2.000233 s, floored to the microsecond.
        Assert.Equal(2_000_233, input.SeekMicros);
        Assert.StartsWith("[0:0]", render.GraphText.Split('\n')[1], StringComparison.Ordinal);
    }
}
