using Cutlass.Media.Services;

namespace Cutlass.Tests.Media.Cache;

/// <summary>The waveform peak pyramid math and its <c>peaks.bin</c> format (TECH_BRIEF 6.1, 6.5, 11.1).</summary>
public sealed class PeakPyramidTests
{
    [Fact]
    public void Level0TakesMinAndMaxOver160SampleBlocksIncludingAPartialLastBlock()
    {
        // 3.5 blocks: block b holds the values b*1000 - 80 .. b*1000 + 79 (a ramp), the last block is half full.
        var samples = new short[(160 * 3) + 80];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(((i / 160) * 1000) - 80 + (i % 160));
        }

        var pyramid = PeakPyramid.FromSamples(samples);

        Assert.Equal(100, pyramid.PeaksPerSecond);
        Assert.Equal(4, pyramid.Count(0));
        Assert.Equal(((short)-80, (short)79), pyramid.Peak(0, 0));
        Assert.Equal(((short)920, (short)1079), pyramid.Peak(0, 1));
        Assert.Equal(((short)2920, (short)2999), pyramid.Peak(0, 3));
    }

    [Fact]
    public void EachLevelHalvesThePreviousOneAndCarriesALoneLastPeak()
    {
        short[] level0 = [-1, 1, -5, 2, -3, 9, -2, 4, -7, 3];
        var pyramid = PeakPyramid.FromLevel0(100, level0);

        Assert.Equal(4, pyramid.LevelCount);
        Assert.Equal(new[] { 5, 3, 2, 1 }, Enumerable.Range(0, pyramid.LevelCount).Select(pyramid.Count));
        Assert.Equal(((short)-5, (short)2), pyramid.Peak(1, 0));
        Assert.Equal(((short)-3, (short)9), pyramid.Peak(1, 1));
        Assert.Equal(((short)-7, (short)3), pyramid.Peak(1, 2));
        Assert.Equal(((short)-5, (short)9), pyramid.Peak(2, 0));
        Assert.Equal(((short)-7, (short)3), pyramid.Peak(2, 1));
        Assert.Equal(((short)-7, (short)9), pyramid.Peak(3, 0));
    }

    [Fact]
    public void ALongWaveformHasLog2Levels()
    {
        // 60 s at 16 kHz: 6000 level 0 peaks, then 3000, 1500, 750, 375, 188, 94, 47, 24, 12, 6, 3, 2, 1.
        var pyramid = PeakPyramid.FromSamples(new short[16000 * 60]);

        Assert.Equal(6000, pyramid.Count(0));
        Assert.Equal(14, pyramid.LevelCount);
        Assert.Equal(1, pyramid.Count(pyramid.LevelCount - 1));
        Assert.Equal(60.0, pyramid.DurationSeconds);
    }

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(1, 0)]
    [InlineData(1.99, 0)]
    [InlineData(2, 1)]
    [InlineData(7.9, 2)]
    [InlineData(8, 3)]
    [InlineData(1e9, 13)]
    public void LevelForPicksTheCoarsestLevelWithAtLeastOnePeakPerBin(double peaksPerBin, int expected)
    {
        var pyramid = PeakPyramid.FromSamples(new short[16000 * 60]);

        Assert.Equal(expected, pyramid.LevelFor(peaksPerBin));
    }

    [Fact]
    public void PeaksForAggregatesEveryOverlappedPeakSoTransientsNeverVanish()
    {
        // 10 s of silence with one spike in level 0 peak 537 (5.37 s).
        var samples = new short[16000 * 10];
        samples[(537 * 160) + 7] = 12000;
        samples[(537 * 160) + 9] = -11000;
        var pyramid = PeakPyramid.FromSamples(samples);

        // 10 s in 37 bins: about 27 peaks per bin, so level 4 (16 level 0 peaks per peak). A level 4 peak may
        // straddle two bins, so the spike shows in the bin holding 5.37 s and at most one neighbour, at full height.
        var bins = pyramid.PeaksFor(0, 48000L * 10, 37);

        Assert.Equal(37, bins.Length);
        Assert.Equal(4, pyramid.LevelFor(1000.0 / 37));
        var loud = bins.Select((b, i) => (b, i)).Where(x => x.b.Max != 0 || x.b.Min != 0).ToList();
        Assert.InRange(loud.Count, 1, 2);
        Assert.All(loud, x => Assert.Equal(((short)-11000, (short)12000), x.b));
        Assert.Contains(loud, x => x.i == (int)(5.37 * 37 / 10));
    }

    [Fact]
    public void PeaksForUsesLevel0WhenZoomedInAndRepeatsAPeakAcrossNarrowBins()
    {
        var samples = new short[16000];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)(i / 160);
        }

        var pyramid = PeakPyramid.FromSamples(samples);

        // Peak 10 covers 48 kHz samples [4800, 5280); four bins inside it all show it.
        var bins = pyramid.PeaksFor(4800, 5280, 4);
        Assert.All(bins, b => Assert.Equal(((short)10, (short)10), b));

        // Two peaks per bin at level 0 would be level 1: peaks (0..1), (2..3).
        var coarse = pyramid.PeaksFor(0, 480 * 4, 2);
        Assert.Equal(((short)0, (short)1), coarse[0]);
        Assert.Equal(((short)2, (short)3), coarse[1]);
    }

    [Fact]
    public void PeaksForOutsideTheAudioIsSilentAndEmptyRangesGiveNothing()
    {
        var pyramid = PeakPyramid.FromSamples(Enumerable.Repeat((short)500, 1600).ToArray());

        var bins = pyramid.PeaksFor(-4800, 9600, 3);
        Assert.Equal(((short)0, (short)0), bins[0]);
        Assert.Equal(((short)500, (short)500), bins[1]);
        Assert.Equal(((short)0, (short)0), bins[2]);
        Assert.Empty(pyramid.PeaksFor(100, 100, 10));
        Assert.Empty(pyramid.PeaksFor(0, 100, 0));
    }

    [Fact]
    public void AnEmptyWaveformHasOneEmptyLevel()
    {
        var pyramid = PeakPyramid.FromSamples([]);

        Assert.Equal(1, pyramid.LevelCount);
        Assert.Equal(0, pyramid.Count(0));
        Assert.All(pyramid.PeaksFor(0, 48000, 5), b => Assert.Equal(((short)0, (short)0), b));
    }

    [Fact]
    public void TheBuilderGivesTheSameResultForAnyChunkingOfTheBytes()
    {
        var random = new Random(7);
        var samples = new short[16000 * 3 + 33];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (short)random.Next(short.MinValue, short.MaxValue + 1);
        }

        var bytes = new byte[samples.Length * 2];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var builder = new PeakPyramid.Builder();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var n = Math.Min(bytes.Length - offset, 2 * random.Next(1, 3000));
            builder.AppendBytes(bytes.AsSpan(offset, n));
            offset += n;
        }

        var expected = PeakPyramid.FromSamples(samples);
        var actual = builder.Build();

        Assert.Equal(samples.Length, builder.SampleCount);
        AssertSame(expected, actual);
    }

    [Fact]
    public void PeaksBinRoundTripsWithTheDocumentedLayout()
    {
        var pyramid = PeakPyramid.FromLevel0(100, [-1, 1, -5, 2, -3, 9]);
        using var stream = new MemoryStream();
        pyramid.WriteTo(stream);
        var bytes = stream.ToArray();

        // "CTLP" u16 1, i32 100, i32 levels(3), then (i32 n, pairs) per level: 3, 2, 1 pairs.
        Assert.Equal("CTLP"u8.ToArray(), bytes[..4]);
        Assert.Equal(1, BitConverter.ToUInt16(bytes, 4));
        Assert.Equal(100, BitConverter.ToInt32(bytes, 6));
        Assert.Equal(3, BitConverter.ToInt32(bytes, 10));
        Assert.Equal(3, BitConverter.ToInt32(bytes, 14));
        Assert.Equal(14 + (3 * 4) + (4 * 3) + (4 * 2) + (4 * 1), bytes.Length);

        stream.Position = 0;
        var read = PeakPyramid.ReadFrom(stream);
        Assert.NotNull(read);
        AssertSame(pyramid, read);
    }

    [Fact]
    public void ACorruptOrForeignFileReadsAsNull()
    {
        Assert.Null(PeakPyramid.ReadFrom(new MemoryStream("CTLX\u0001\u0000"u8.ToArray())));
        Assert.Null(PeakPyramid.ReadFrom(new MemoryStream([])));

        var pyramid = PeakPyramid.FromLevel0(100, [-1, 1, -5, 2]);
        using var stream = new MemoryStream();
        pyramid.WriteTo(stream);
        var truncated = stream.ToArray()[..^2];
        Assert.Null(PeakPyramid.ReadFrom(new MemoryStream(truncated)));
    }

    private static void AssertSame(PeakPyramid expected, PeakPyramid actual)
    {
        Assert.Equal(expected.PeaksPerSecond, actual.PeaksPerSecond);
        Assert.Equal(expected.LevelCount, actual.LevelCount);
        for (var level = 0; level < expected.LevelCount; level++)
        {
            Assert.Equal(expected.Count(level), actual.Count(level));
            for (var i = 0; i < expected.Count(level); i++)
            {
                Assert.Equal(expected.Peak(level, i), actual.Peak(level, i));
            }
        }
    }
}
