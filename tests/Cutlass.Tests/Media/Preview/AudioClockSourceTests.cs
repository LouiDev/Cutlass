using Cutlass.Media.Preview.Audio;

namespace Cutlass.Tests.Media.Preview;

/// <summary>
/// The clock falls back to a Stopwatch when the audio output is lost (TECH_BRIEF 5.6 and 8.2): a disconnected device's
/// played position races ahead and must not move the video.
/// </summary>
public sealed class AudioClockSourceTests
{
    private const long Frequency = 1_000_000;

    [Fact]
    public void AnAvailableOutputDrivesTheClock()
    {
        var output = new FakeAudioOutput();
        var source = new AudioClockSource(output);
        source.Start(0);

        output.PlayedSamples = 4800;
        Assert.Equal(4800, source.PlayedSamples(100_000, Frequency));
        Assert.False(source.FellBack);
        Assert.False(source.TakeLost());
    }

    [Fact]
    public void ALostOutputContinuesFromTheLastRealPositionAtRealTime()
    {
        var output = new FakeAudioOutput();
        var source = new AudioClockSource(output);
        source.Start(0);
        output.PlayedSamples = 48_000;
        Assert.Equal(48_000, source.PlayedSamples(1_000_000, Frequency));

        // The device is unplugged: its position jumps to the end of the queue.
        output.IsAvailable = false;
        output.PlayedSamples = 10_000_000;
        source.Poll(1_100_000);

        Assert.True(source.FellBack);
        Assert.True(source.TakeLost());
        Assert.False(source.TakeLost());
        Assert.Equal(48_000, source.PlayedSamples(1_100_000, Frequency));
        Assert.Equal(48_000 + 24_000, source.PlayedSamples(1_600_000, Frequency));
    }

    [Fact]
    public void LossNoticedDuringAReadKeepsThePreviousValue()
    {
        var output = new FakeAudioOutput();
        var source = new AudioClockSource(output);
        source.Start(0);
        output.PlayedSamples = 960;
        Assert.Equal(960, source.PlayedSamples(20_000, Frequency));

        output.IsAvailable = false;
        output.PlayedSamples = 99_999;

        Assert.Equal(960, source.PlayedSamples(40_000, Frequency));
        Assert.True(source.TakeLost());
    }

    [Fact]
    public void AnOutputUnavailableFromTheStartIsFedAsUsual()
    {
        // NullAudioOutput reports itself unavailable and plays on its own Stopwatch: nothing to fall back from.
        var output = new FakeAudioOutput { IsAvailable = false };
        var source = new AudioClockSource(output);
        source.Start(0);
        output.PlayedSamples = 4800;
        source.Poll(100_000);

        Assert.False(source.FellBack);
        Assert.Equal(4800, source.PlayedSamples(100_000, Frequency));
        Assert.False(source.TakeLost());
    }
}
