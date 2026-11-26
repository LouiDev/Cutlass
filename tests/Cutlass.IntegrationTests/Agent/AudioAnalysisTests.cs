using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Api.Requests.Clips;
using Cutlass.Agent.Api.Requests.Media;
using Cutlass.Agent.Api.Requests.Project;
using Cutlass.Agent.Tools.Analysis;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>Mix levels, silence detection and remove-ranges on real audio (AGENT_INTEGRATION 7.2).</summary>
[Collection(TestMediaCollection.Name)]
public sealed class AudioAnalysisTests
{
    private readonly TestMediaFixture _media;

    public AudioAnalysisTests(TestMediaFixture media)
    {
        _media = media;
    }

    private static async Task<AgentKit> StartAsync(TestMediaFixture media)
    {
        var kit = await AgentKit.CreateAsync(media);
        await kit.Client.AcquireLockAsync("Integration", "Audio test");
        await kit.Client.NewProjectAsync(new NewProjectRequest { Width = 320, Height = 180, FrameRate = "25" });
        return kit;
    }

    [FfmpegFact]
    public async Task ToneSilenceToneYieldsOneSilenceWithinAFrame()
    {
        await using var kit = await StartAsync(_media);
        var gate = await kit.AudioAsync("gate.wav", "if(between(t\\,1\\,2.5)\\,0\\,0.5*sin(2*PI*440*t))", 4);
        var media = (await kit.Client.ImportMediaAsync([gate])).Imported.Single();
        await kit.Client.PlaceAsync(new PlaceRequest { MediaId = media.Id, At = 0 });
        var silences = await kit.Client.FindSilencesAsync(new SilencesRequest { PadMs = 0, MinDurationMs = 500 });
        var range = Assert.Single(silences.Ranges);
        Assert.InRange(range.Start, 24, 26);   // 1.0 s at 25 fps
        Assert.InRange(range.End, 61, 63);     // 2.5 s
        Assert.InRange(range.Seconds, 1.4, 1.6);

        // With padding the range shrinks on both sides.
        var padded = Assert.Single((await kit.Client.FindSilencesAsync(new SilencesRequest { PadMs = 200 })).Ranges);
        Assert.InRange(padded.Start, range.Start + 4, range.Start + 6);
        Assert.InRange(padded.End, range.End - 6, range.End - 4);
    }

    [FfmpegFact]
    public async Task ToneLevelsMatchTheFileWithinHalfADecibel()
    {
        await using var kit = await StartAsync(_media);
        var media = (await kit.Client.ImportMediaAsync([_media.Tone])).Imported.Single();
        await kit.Client.PlaceAsync(new PlaceRequest { MediaId = media.Id, At = 0 });

        // The file's own level, decoded by ffmpeg into the mix layout the compiler uses (aformat to stereo, which puts a
        // mono source on both channels at -3 dB): the sine at 1/8 of full scale reads -21.07 dBFS mono, -24.08 in the mix.
        var decoded = await TestProcess.RunAsync(
            TestFfmpeg.FfmpegPath,
            ["-hide_banner", "-v", "error", "-i", _media.Tone, "-t", "8", "-af", "aformat=sample_fmts=s16:sample_rates=48000:channel_layouts=stereo", "-f", "s16le", "pipe:1"],
            kit.MediaDirectory,
            TimeSpan.FromMinutes(1));
        var analyzer = new LevelAnalyzer(100);
        analyzer.Add(decoded.StdoutBytes);
        var fileRms = analyzer.Complete().RmsDb.Skip(1).SkipLast(1).Average();

        var levels = await kit.Client.AudioLevelsAsync(new AudioLevelsRequest { Start = 0, End = 200, IntervalMs = 100 });
        Assert.Equal(80, levels.RmsDb.Count);
        var mixRms = levels.RmsDb.Skip(1).SkipLast(1).Average();
        Assert.InRange(mixRms, fileRms - 0.5, fileRms + 0.5);
        Assert.InRange(fileRms, -24.6, -23.5);

        // The source levels of the media read the same.
        var source = await kit.Client.MediaLevelsAsync(new MediaLevelsRequest { MediaId = media.Id, StartSeconds = 0, EndSeconds = 8, IntervalMs = 100 });
        Assert.InRange(source.RmsDb.Skip(1).SkipLast(1).Average(), fileRms - 0.5, fileRms + 0.5);

        // A soloed empty track reads the floor.
        var a2 = (await kit.Client.GetProjectAsync()).Tracks.Single(t => t.Kind == "audio" && t.Index == 2);
        var silent = await kit.Client.AudioLevelsAsync(new AudioLevelsRequest { Start = 0, End = 50, IntervalMs = 100, TrackIds = [a2.Id] });
        Assert.All(silent.RmsDb, r => Assert.Equal(LevelAnalyzer.FloorDb, r));
    }

    [FfmpegFact]
    public async Task RemoveRangesShortensTheTimelineByExactlyTheRange()
    {
        await using var kit = await StartAsync(_media);
        var media = (await kit.Client.ImportMediaAsync([_media.Tone])).Imported.Single();
        await kit.Client.PlaceAsync(new PlaceRequest { MediaId = media.Id, At = 0 });
        var before = (await kit.Client.GetProjectAsync()).Sequence.DurationFrames;
        Assert.Equal(250, before);
        var r = await kit.Client.RemoveRangesAsync(new RemoveRangesRequest { Ranges = [new FrameRange { Start = 50, End = 100 }] });
        Assert.True(r.Changed);
        Assert.Equal(before - 50, (await kit.Client.GetProjectAsync()).Sequence.DurationFrames);
    }
}
