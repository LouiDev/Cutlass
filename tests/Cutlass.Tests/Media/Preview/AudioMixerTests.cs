using System.Collections.Immutable;
using Cutlass.Core.Model;
using Cutlass.Media.Preview;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Media.Preview;

/// <summary>
/// The live mixer math of TECH_BRIEF 8.2: <c>out = clamp(round(sum(active_t * in_t * 10^(gainDb_t/20)) * master))</c>,
/// mute and solo rules, the Mixed fallback and peak levels.
/// </summary>
public sealed class AudioMixerTests
{
    private static readonly string[] Ids = ["tr_a1", "tr_a2", "tr_a3"];

    [Fact]
    public void UnityTracksAreSummed()
    {
        var mixer = new AudioMixer(Ids);
        var output = Mix(mixer, [100, -200, 300, 400, -50, 60]);

        Assert.Equal(new short[] { 350, 260 }, output);
    }

    [Fact]
    public void MuteRemovesATrack()
    {
        var mixer = new AudioMixer(Ids, State(("tr_a2", true, false, 0)));
        var output = Mix(mixer, [100, -200, 300, 400, -50, 60]);

        Assert.Equal(new short[] { 50, -140 }, output);
    }

    [Fact]
    public void SoloKeepsOnlySoloedTracksAndMuteWinsOverSolo()
    {
        var soloOne = new AudioMixer(Ids, State(("tr_a3", false, true, 0)));
        Assert.Equal(new short[] { -50, 60 }, Mix(soloOne, [100, -200, 300, 400, -50, 60]));

        var soloTwo = new AudioMixer(Ids, State(("tr_a1", false, true, 0), ("tr_a3", false, true, 0)));
        Assert.Equal(new short[] { 50, -140 }, Mix(soloTwo, [100, -200, 300, 400, -50, 60]));

        // A soloed track that is also muted stays silent, and the others stay silent because something is soloed.
        var mutedSolo = new AudioMixer(Ids, State(("tr_a1", true, true, 0)));
        Assert.Equal(new short[] { 0, 0 }, Mix(mutedSolo, [100, -200, 300, 400, -50, 60]));
    }

    [Fact]
    public void GainIsAppliedInDecibelsAndRounded()
    {
        var mixer = new AudioMixer(["tr_a1"], State(("tr_a1", false, false, -6.0)));
        var factor = Math.Pow(10, -6.0 / 20);

        var output = Mix(mixer, [1000, -1001]);

        Assert.Equal((short)Math.Round(1000 * factor, MidpointRounding.AwayFromZero), output[0]);
        Assert.Equal((short)Math.Round(-1001 * factor, MidpointRounding.AwayFromZero), output[1]);
        Assert.Equal(501, output[0]);
        Assert.Equal(-502, output[1]);
    }

    [Fact]
    public void MasterScalesTheSumAndTheOutputClamps()
    {
        var mixer = new AudioMixer(Ids, new MixerState([], 0.5));
        Assert.Equal(new short[] { 175, 130 }, Mix(mixer, [100, -200, 300, 400, -50, 60]));

        var loud = new AudioMixer(Ids, new MixerState([], 1.0));
        Assert.Equal(new short[] { short.MaxValue, short.MinValue }, Mix(loud, [30000, -30000, 30000, -30000, 0, 0]));
    }

    [Fact]
    public void MixerChangesApplyToTheNextBlockWithoutRebuilding()
    {
        var mixer = new AudioMixer(Ids);
        var input = new short[] { 100, 100, 200, 200, 400, 400 };
        Assert.Equal(new short[] { 700, 700 }, Mix(mixer, input));

        mixer.SetState(State(("tr_a3", true, false, 0)));
        Assert.Equal(new short[] { 300, 300 }, Mix(mixer, input));

        mixer.SetState(mixer.State.WithTrack(new MixerTrack("tr_a1", false, true, 6.0)));
        var expected = (short)Math.Round(100 * Math.Pow(10, 6.0 / 20), MidpointRounding.AwayFromZero);
        Assert.Equal(new[] { expected, expected }, Mix(mixer, input));
    }

    [Fact]
    public void UnknownTracksPlayAtUnityAndTrackIdsMapByIdNotPosition()
    {
        // The state lists the tracks in another order and leaves tr_a2 out.
        var state = new MixerState([new MixerTrack("tr_a3", false, false, 0), new MixerTrack("tr_a1", true, false, 0)], 1.0);
        var mixer = new AudioMixer(Ids, state);

        Assert.Equal(new[] { 0.0, 1.0, 1.0 }, mixer.TrackFactors());
        Assert.Equal(new short[] { 250, 460 }, Mix(mixer, [100, -200, 300, 400, -50, 60]));
    }

    [Fact]
    public void ATimelineWithoutAudioTracksMixesOneSilentPair()
    {
        var mixer = new AudioMixer([]);
        Assert.Equal(2, mixer.InputChannels);
        Assert.Equal(new short[] { 0, 0, 0, 0 }, Mix(mixer, [0, 0, 0, 0], frames: 2));
    }

    [Fact]
    public void PremixedInputOnlyTakesTheMaster()
    {
        var mixer = new AudioMixer(Ids, new MixerState([new MixerTrack("tr_a1", true, false, -20)], 0.5)) { Premixed = true };

        Assert.Equal(2, mixer.InputChannels);
        Assert.Equal(new short[] { 500, -500 }, Mix(mixer, [1000, -1000]));

        mixer.Premixed = false;
        Assert.Equal(6, mixer.InputChannels);
    }

    [Fact]
    public void PeaksArePerTrackAfterGainAndPerMasterChannelAndReset()
    {
        var mixer = new AudioMixer(Ids, State(("tr_a2", true, false, 0), ("tr_a3", false, false, -6.0206)));
        var input = new short[]
        {
            16384, -8192, 32000, 0, 0, -32768,
            -4096, 4096, 0, 0, 16384, 0,
        };
        Mix(mixer, input, frames: 2);

        var peaks = mixer.ReadAndResetPeaks();

        Assert.Equal(3, peaks.Tracks.Length);
        Assert.Equal(0.5f, peaks.Tracks[0], 3);
        Assert.Equal(0f, peaks.Tracks[1]);
        Assert.Equal(0.5f, peaks.Tracks[2], 3);
        Assert.Equal(0.5f, peaks.MasterLeft, 3);
        Assert.Equal(0.75f, peaks.MasterRight, 3);

        var after = mixer.ReadAndResetPeaks();
        Assert.All(after.Tracks, p => Assert.Equal(0f, p));
        Assert.Equal(0f, after.MasterLeft);
        Assert.Equal(0f, after.MasterRight);
    }

    [Fact]
    public void DbfsConversionIsFlooredForSilence()
    {
        Assert.Equal(0.0, MixerPeaks.ToDbfs(1f), 6);
        Assert.Equal(-6.0206, MixerPeaks.ToDbfs(0.5f), 3);
        Assert.Equal(MixerPeaks.FloorDbfs, MixerPeaks.ToDbfs(0f));
        Assert.Equal(MixerPeaks.FloorDbfs, MixerPeaks.ToDbfs(1e-9f));
    }

    [Fact]
    public void ShortBuffersAreRejected()
    {
        var mixer = new AudioMixer(Ids);
        Assert.Throws<ArgumentException>(() => mixer.Mix(new short[5], new short[2], 1));
        Assert.Throws<ArgumentException>(() => mixer.Mix(new short[6], new short[1], 1));
    }

    [Fact]
    public void TheStateFollowsTheSequenceAndCanBeBakedIntoAProject()
    {
        var project = new ProjectBuilder().Build();

        var state = MixerState.FromSequence(project.Sequence);
        Assert.Equal(new[] { ProjectBuilder.A1, ProjectBuilder.A2 }, state.Tracks.Select(t => t.TrackId));
        Assert.False(state.AnySolo);

        var changed = state.WithTrack(new MixerTrack(ProjectBuilder.A1, true, false, -3));
        var baked = changed.ApplyTo(project);
        var a1 = baked.Sequence.Tracks.Single(t => t.Id == ProjectBuilder.A1);
        Assert.True(a1.Muted);
        Assert.Equal(-3, a1.GainDb);
        Assert.Equal(project.Sequence.Tracks.Single(t => t.Id == ProjectBuilder.V1), baked.Sequence.Tracks.Single(t => t.Id == ProjectBuilder.V1));
        Assert.False(changed.IsActive(ProjectBuilder.A1));
        Assert.True(changed.IsActive(ProjectBuilder.A2));

        var soloA2 = changed.WithTrack(new MixerTrack(ProjectBuilder.A2, false, true, 0));
        Assert.True(soloA2.AnySolo);
        Assert.False(soloA2.IsActive("tr_unknown"));
        Assert.Equal(2, soloA2.Tracks.Length);
    }

    [Fact]
    public async Task PremixedAndStateChangesFromTwoThreadsStayConsistent()
    {
        // The feeder flips Premixed while the UI sets the state; neither may undo the other.
        for (var round = 0; round < 200; round++)
        {
            var mixer = new AudioMixer(Ids);
            using var go = new ManualResetEventSlim();
            var setter = Task.Run(() =>
            {
                go.Wait();
                for (var i = 0; i < 50; i++)
                {
                    mixer.SetState(State(("tr_a1", false, false, -i)));
                }
            });
            var flipper = Task.Run(() =>
            {
                go.Wait();
                mixer.Premixed = true;
            });
            go.Set();
            await Task.WhenAll(setter, flipper);

            Assert.True(mixer.Premixed);
            Assert.Equal(2, mixer.InputChannels);
            Assert.Equal(-49, mixer.State.Find("tr_a1")!.GainDb);
            Assert.Equal(new[] { 1.0 }, mixer.TrackFactors());
        }
    }

    private static MixerState State(params (string Id, bool Muted, bool Solo, double GainDb)[] tracks) =>
        new([.. tracks.Select(t => new MixerTrack(t.Id, t.Muted, t.Solo, t.GainDb))], 1.0);

    private static short[] Mix(AudioMixer mixer, short[] input, int frames = 1)
    {
        var output = new short[frames * 2];
        mixer.Mix(input, output, frames);
        return output;
    }
}
