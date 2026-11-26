using Cutlass.Agent.Tools.Analysis;

namespace Cutlass.Tests.Agent;

/// <summary>The loudness analyzer (AGENT_INTEGRATION 7.1).</summary>
public sealed class LevelAnalyzerTests
{
    internal static byte[] Pcm(int samples, Func<int, short> left, Func<int, short>? right = null)
    {
        var bytes = new byte[samples * 4];
        for (var i = 0; i < samples; i++)
        {
            var l = left(i);
            var r = (right ?? left)(i);
            bytes[i * 4] = (byte)l;
            bytes[(i * 4) + 1] = (byte)(l >> 8);
            bytes[(i * 4) + 2] = (byte)r;
            bytes[(i * 4) + 3] = (byte)(r >> 8);
        }

        return bytes;
    }

    internal static short Sine(int i, double amplitude = 32767, double hz = 1000) =>
        (short)Math.Round(amplitude * Math.Sin(2 * Math.PI * hz * i / 48_000));

    [Fact]
    public void AFullScaleSineReadsMinusThreeDecibelsRms()
    {
        var a = new LevelAnalyzer(50);
        a.Add(Pcm(48_000, i => Sine(i)));
        var (rms, peak) = a.Complete();
        Assert.Equal(20, rms.Count);
        Assert.All(rms, r => Assert.InRange(r, -3.03, -2.99));
        Assert.All(peak, p => Assert.InRange(p, -0.01, 0));
    }

    [Fact]
    public void SilenceReadsTheFloor()
    {
        var a = new LevelAnalyzer(10);
        a.Add(Pcm(4800, _ => 0));
        var (rms, peak) = a.Complete();
        Assert.Equal(10, rms.Count);
        Assert.All(rms, r => Assert.Equal(LevelAnalyzer.FloorDb, r));
        Assert.All(peak, p => Assert.Equal(LevelAnalyzer.FloorDb, p));
    }

    [Fact]
    public void ChunksSplitInsideASampleFrameAreJoinedAndThePartialIntervalCounts()
    {
        var pcm = Pcm(2500, i => Sine(i, 16384));
        var whole = new LevelAnalyzer(50);
        whole.Add(pcm);
        var split = new LevelAnalyzer(50);
        for (var i = 0; i < pcm.Length; i += 7)
        {
            split.Add(pcm.AsSpan(i, Math.Min(7, pcm.Length - i)));
        }

        var (a, _) = whole.Complete();
        var (b, _) = split.Complete();
        Assert.Equal(2, a.Count);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TheSampleLimitStopsTheAnalysis()
    {
        var a = new LevelAnalyzer(10, 480);
        a.Add(Pcm(4800, i => Sine(i)));
        Assert.Single(a.Complete().RmsDb);
    }
}
