using Cutlass.Core.Time;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>Checks shared by the playback tests (TECH_BRIEF 11.2: seams and A/V sync).</summary>
internal static class PlaybackAssert
{
    /// <summary>An impulse (0.9 full scale) is far above this; silence and the tone are far below.</summary>
    public const short ImpulseThreshold = 16000;

    /// <summary>One Matroska timestamp tick (1 ms) in samples: the audio seek precision inside an mkv source.</summary>
    public const long MatroskaSampleTolerance = 48;

    /// <summary>The sync clip's picture at its centre: 1 on a flash frame (luma 235), 0 otherwise (luma 16).</summary>
    public static int Flash(byte[] bgra, int width, int height)
    {
        var offset = ((((height / 2) * width) + (width / 2)) * 4) + 1;
        return bgra[offset] >= 128 ? 1 : 0;
    }

    /// <summary>The stereo frame indices of every impulse in a recording (left channel).</summary>
    public static List<long> ImpulsePositions(short[] stereo, long fromFrame = 0, long toFrame = long.MaxValue)
    {
        var positions = new List<long>();
        var frames = Math.Min(stereo.Length / 2, toFrame);
        for (var i = fromFrame; i < frames; i++)
        {
            if (Math.Abs((int)stereo[2 * i]) > ImpulseThreshold)
            {
                positions.Add(i);
            }
        }

        return positions;
    }

    /// <summary>
    /// The A/V sync and seam checks for a timeline of sync clips starting at frame 0 (flash on frames 30k, impulse at
    /// sample 48000k, which is <c>SamplesAt(30k)</c>):
    /// the heard audio has every impulse at its exact sample, the presented frames are strictly increasing and, with
    /// the drops, account for every frame once (no gap, no duplicate), each picture matches its label, and every
    /// presented frame is within one frame of the audio clock it was taken for.
    /// </summary>
    public static void SyncPlayback(IReadOnlyList<FakePresenter.Presentation> presented, long dropped, short[] recorded, int seconds)
    {
        var totalFrames = seconds * 30L;
        var rate = FrameRate.Fps30;

        // Audio: every window is exactly SamplesAt(f1) - SamplesAt(f0) long, so nothing drifts. Inside a window the
        // content sits where its seek put it: sync.mkv is Matroska, whose 1 ms packet timestamps let a mid-file audio
        // seek land up to 1 ms (48 samples) off, so impulses after a mid-clip seam may move by that much.
        Assert.Equal(TimeMath.SamplesAt(totalFrames, rate) * 2, recorded.LongLength);
        var expectedImpulses = Enumerable.Range(0, seconds).Select(k => TimeMath.SamplesAt(30L * k, rate)).ToList();
        var impulses = ImpulsePositions(recorded);
        Assert.Equal(expectedImpulses.Count, impulses.Count);
        for (var k = 0; k < impulses.Count; k++)
        {
            Assert.InRange(impulses[k] - expectedImpulses[k], -MatroskaSampleTolerance, MatroskaSampleTolerance);
        }

        // Video: strictly increasing, every frame either presented or dropped exactly once.
        Assert.NotEmpty(presented);
        for (var i = 1; i < presented.Count; i++)
        {
            Assert.True(presented[i].Frame > presented[i - 1].Frame, $"Frame {presented[i].Frame} followed {presented[i - 1].Frame}.");
        }

        Assert.Equal(totalFrames, presented.Count + dropped);
        Assert.Equal(totalFrames - 1, presented[^1].Frame);

        // Pictures match their labels: the flash is on frames 30k and nowhere else (a seam off by one would move it).
        foreach (var p in presented)
        {
            Assert.True((p.Frame % 30 == 0 ? 1 : 0) == p.Value, $"Frame {p.Frame} shows flash={p.Value}.");
        }

        // A/V sync: each frame was taken for a clock frame at most one frame later than itself.
        var lags = presented.Select(p => p.Target - p.Frame).ToList();
        Assert.All(lags, lag => Assert.InRange(lag, 0, 1));

        // Every flash (each impulse's frame) was presented, not dropped.
        var flashes = presented.Where(p => p.Value == 1).Select(p => p.Frame).ToList();
        Assert.Equal(Enumerable.Range(0, seconds).Select(k => 30L * k), flashes);
    }
}
