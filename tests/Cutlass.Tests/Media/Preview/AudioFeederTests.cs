using System.Threading.Channels;
using Cutlass.Media.Preview.Audio;

namespace Cutlass.Tests.Media.Preview;

/// <summary>
/// The feeder's output side (TECH_BRIEF 8.2): empty blocks never reach the output (the PerTrack to Mixed fallback
/// leaves one behind, and OpenAL rejects an empty buffer), the mixer follows each block's layout, and a lost output's
/// blocks are dropped at the rate they would have played.
/// </summary>
public sealed class AudioFeederTests
{
    [Fact]
    public void AnEmptyBlockLeftByTheMixedFallbackIsSkipped()
    {
        var output = new FakeAudioOutput();
        var mixer = new AudioMixer(["tr_a1", "tr_a2"]);
        var channel = Channel.CreateUnbounded<PcmBlock>();
        channel.Writer.TryWrite(new PcmBlock(1024, 4, premixed: false));
        channel.Writer.TryWrite(Block(1024, 2, premixed: true, frames: 1024));
        PcmBlock? pending = null;

        var enqueued = AudioFeeder.FeedOutput(output, mixer, channel.Reader, ref pending, new short[2048]);

        Assert.Equal(1024, enqueued);
        Assert.Equal([1024], output.Enqueued);
        Assert.True(mixer.Premixed);
        Assert.Null(pending);
    }

    [Fact]
    public void ABlockThatDoesNotFitStaysPending()
    {
        var output = new FakeAudioOutput { FreeBuffers = 1 };
        var mixer = new AudioMixer(["tr_a1"]);
        var channel = Channel.CreateUnbounded<PcmBlock>();
        channel.Writer.TryWrite(Block(1024, 2, premixed: false, frames: 1024));
        channel.Writer.TryWrite(Block(1024, 2, premixed: false, frames: 512));
        PcmBlock? pending = null;

        Assert.Equal(1024, AudioFeeder.FeedOutput(output, mixer, channel.Reader, ref pending, new short[2048]));

        // FreeBuffers reached 0 before the second block was read.
        Assert.Null(pending);
        output.FreeBuffers = 1;
        Assert.Equal(512, AudioFeeder.FeedOutput(output, mixer, channel.Reader, ref pending, new short[2048]));
        Assert.Equal([1024, 512], output.Enqueued);
    }

    [Fact]
    public void DiscardingStopsAtTheBudget()
    {
        var channel = Channel.CreateUnbounded<PcmBlock>();
        for (var i = 0; i < 4; i++)
        {
            channel.Writer.TryWrite(Block(1024, 2, premixed: false, frames: 1024));
        }

        PcmBlock? pending = null;

        Assert.Equal(2048, AudioFeeder.DiscardBlocks(channel.Reader, ref pending, 1500));
        Assert.Equal(0, AudioFeeder.DiscardBlocks(channel.Reader, ref pending, 0));
        Assert.Equal(2048, AudioFeeder.DiscardBlocks(channel.Reader, ref pending, 10_000));
    }

    private static PcmBlock Block(int capacity, int channels, bool premixed, int frames) =>
        new(capacity, channels, premixed) { Frames = frames };
}
