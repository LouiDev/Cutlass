using Cutlass.Core.Time;
using Cutlass.Media.Audio;
using Cutlass.Media.Preview;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Media.Preview;

/// <summary>
/// Window planning of TECH_BRIEF 8.2: 10 s * Speed of frames per window, output frame k is <c>start + k * Speed</c>,
/// <c>ceil(n / Speed)</c> frames per window, window audio <c>SamplesAt(f1) - SamplesAt(f0)</c> so seams are gapless,
/// and the 3 s prefetch threshold. Also the silent fallback output and option validation.
/// </summary>
public sealed class PlaybackWindowTests
{
    [Theory]
    [InlineData(30, 1, 1, 300)]
    [InlineData(30, 1, 2, 600)]
    [InlineData(30, 1, 4, 1200)]
    [InlineData(25, 1, 1, 250)]
    [InlineData(30000, 1001, 1, 299)]
    [InlineData(30000, 1001, 4, 1196)]
    [InlineData(60000, 1001, 2, 1198)]
    public void AWindowCoversTenSecondsTimesSpeedInWholeSteps(long num, long den, int speed, long expected)
    {
        var length = PlaybackWindow.Length(new FrameRate(new Rational(num, den)), speed, 10.0);
        Assert.Equal(expected, length);
        Assert.Equal(0, length % speed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void WindowsTileTheRangeAndTheirOutputFramesAreConsecutiveSteps(int speed)
    {
        foreach (var rate in new[] { FrameRate.Fps25, FrameRate.Fps30, FrameRate.Fps2997, FrameRate.Fps5994 })
        {
            for (long start = 0; start < 40; start += 13)
            {
                var end = start + 2345;
                var windows = PlaybackWindow.Plan(start, end, speed, rate, 10.0);

                Assert.Equal(start, windows[0].StartFrame);
                Assert.Equal(end, windows[^1].EndFrame);
                var frames = new List<long>();
                for (var i = 0; i < windows.Count; i++)
                {
                    var w = windows[i];
                    Assert.Equal(i, w.Index);
                    Assert.Equal(speed, w.Speed);
                    if (i > 0)
                    {
                        // Seams: no gap and no overlap in frames or samples.
                        Assert.Equal(windows[i - 1].EndFrame, w.StartFrame);
                        Assert.Equal(windows[i - 1].EndSample, w.StartSample);
                    }

                    Assert.Equal(TimeMath.SamplesAt(w.StartFrame, rate), w.StartSample);
                    Assert.Equal(TimeMath.SamplesAt(w.EndFrame, rate), w.EndSample);
                    Assert.Equal((w.FrameCount + speed - 1) / speed, w.OutputFrames);
                    for (long k = 0; k < w.OutputFrames; k++)
                    {
                        frames.Add(w.TimelineFrameOf(k));
                    }
                }

                // Every output frame is start + k * speed: the skip pattern runs across seams unbroken.
                Assert.Equal(Enumerable.Range(0, frames.Count).Select(k => start + ((long)k * speed)), frames);
                Assert.True(frames[^1] < end && frames[^1] + speed >= end);
                Assert.Equal(TimeMath.SamplesAt(end, rate) - TimeMath.SamplesAt(start, rate), windows.Sum(w => w.SampleCount));
            }
        }
    }

    [Fact]
    public void AtReturnsNullPastTheEndAndTheLastWindowIsShort()
    {
        Assert.Null(PlaybackWindow.At(1, 0, 300, 1, FrameRate.Fps30, 10.0));
        var last = PlaybackWindow.At(1, 0, 301, 1, FrameRate.Fps30, 10.0);
        Assert.NotNull(last);
        Assert.Equal(300, last.StartFrame);
        Assert.Equal(1, last.FrameCount);

        var odd = PlaybackWindow.At(0, 0, 7, 4, FrameRate.Fps30, 10.0)!;
        Assert.Equal(2, odd.OutputFrames);
        Assert.Equal(4, odd.TimelineFrameOf(1));
    }

    [Fact]
    public void ShortTestWindowsStillAdvanceByWholeSteps()
    {
        var length = PlaybackWindow.Length(FrameRate.Fps25, 4, 0.01);
        Assert.Equal(4, length);
        var windows = PlaybackWindow.Plan(3, 20, 4, FrameRate.Fps25, 0.01);
        Assert.Equal(new long[] { 3, 7, 11, 15, 19 }, windows.Select(w => w.StartFrame));
    }

    [Theory]
    [InlineData(30, 1, 3.0, 90)]
    [InlineData(30000, 1001, 3.0, 90)]
    [InlineData(25, 1, 3.0, 75)]
    [InlineData(25, 1, 0.0, 0)]
    public void TheNextWindowStartsWithThreeSecondsOfOutputLeft(long num, long den, double seconds, long expected)
    {
        Assert.Equal(expected, PlaybackWindow.PrefetchOutputFrames(new FrameRate(new Rational(num, den)), seconds));
    }

    [Fact]
    public void OptionsRejectUnsupportedSpeedsSizesAndRanges()
    {
        var project = new ProjectBuilder().With(ProjectBuilder.V1, TestMedia.V("c_1", 0, 100)).Build();
        var good = PlaybackOptions.ForSequence(project, 10, 2, new PreviewSize(320, 180), useProxies: false);
        good.Validate();
        Assert.Equal(100, good.EndFrame);
        Assert.Equal(10.0, good.WindowSeconds);
        Assert.Equal(3.0, good.PrefetchSeconds);
        Assert.Equal(new[] { 1, 2, 4 }, PlaybackOptions.SupportedSpeeds);

        Assert.Throws<ArgumentException>(() => (good with { Speed = 3 }).Validate());
        Assert.Throws<ArgumentException>(() => (good with { Size = new PreviewSize(321, 180) }).Validate());
        Assert.Throws<ArgumentException>(() => (good with { StartFrame = 100 }).Validate());
        Assert.Throws<ArgumentException>(() => (good with { StartFrame = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (good with { WindowSeconds = 0 }).Validate());
    }

    [Fact]
    public void TheSilentOutputIsAStopwatchThatPacesTheQueue()
    {
        using var output = new NullAudioOutput(bufferCount: 6, bufferFrames: 1024);
        Assert.False(output.IsAvailable);
        output.Open(48000, 2);
        Assert.Equal(6, output.FreeBuffers);

        var block = new short[2048];
        for (var i = 0; i < 6; i++)
        {
            Assert.True(output.Enqueue(block));
        }

        Assert.False(output.Enqueue(block));
        Assert.Equal(0, output.FreeBuffers);
        Assert.Equal(6, output.QueuedBuffers);
        Assert.Equal(0, output.PlayedSamples);

        output.Start();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (output.FreeBuffers == 0)
        {
            Assert.True(DateTime.UtcNow < deadline, "The silent clock did not advance.");
            Thread.Sleep(5);
        }

        var played = output.PlayedSamples;
        Assert.True(played > 0);
        output.Pause();
        var paused = output.PlayedSamples;
        Thread.Sleep(30);
        Assert.Equal(paused, output.PlayedSamples);
        Assert.True(paused >= played);

        output.Flush();
        Assert.Equal(0, output.PlayedSamples);
        Assert.Equal(6, output.FreeBuffers);
        Assert.Throws<ArgumentException>(() => output.Enqueue(new short[3]));
    }
}
