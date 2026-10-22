using System.Collections.Immutable;
using Cutlass.Core.Model;
using Cutlass.Core.Time;
using Cutlass.IntegrationTests.PreviewTests;
using Cutlass.Media.Audio;
using Cutlass.Media.Preview;
using static Cutlass.IntegrationTests.Export.TimelineKit;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>
/// The preview stack of <see cref="PreviewKit"/> plus playback sessions on a <see cref="RecordingAudioOutput"/> (or any
/// output) and helpers that build the playback timelines.
/// </summary>
internal sealed class PlaybackKit : IAsyncDisposable
{
    private readonly List<PlaybackSession> _sessions = [];

    private PlaybackKit(PreviewKit preview)
    {
        Preview = preview;
    }

    /// <summary>The underlying preview kit (harness, catalog, importer, fonts).</summary>
    public PreviewKit Preview { get; }

    /// <summary>The frame cache shared by the sessions of this kit.</summary>
    public FrameCache Cache { get; } = new();

    /// <summary>Creates the kit.</summary>
    public static async Task<PlaybackKit> CreateAsync() => new(await PreviewKit.CreateAsync());

    /// <summary>A session on the given output; the kit disposes it.</summary>
    public PlaybackSession Session(IAudioOutput audio)
    {
        var p = Preview;
        var session = new PlaybackSession(p.Harness.Runner, p.Harness.Toolset, p.Capabilities, p.Harness.Work, p.Catalog, p.Fonts, audio, Cache);
        _sessions.Add(session);
        return session;
    }

    /// <summary>
    /// A 30 fps 320x180 timeline with <paramref name="copies"/> copies of a 10 s file end to end, video on V1 and its
    /// audio on A1 (the sync clip: a flash on every 30th frame and an impulse on every second).
    /// </summary>
    public async Task<Project> RepeatedAvProjectAsync(string file, int copies, long framesPerCopy = 300)
    {
        var asset = (await Preview.Importer.ImportAsync(file, CancellationToken.None)).Asset;
        var video = Enumerable.Range(0, copies).Select(i => (Clip)V($"c_v{i}", i * framesPerCopy, framesPerCopy, asset.Id, 0));
        var audio = Enumerable.Range(0, copies).Select(i => (Clip)A($"c_a{i}", i * framesPerCopy, framesPerCopy, asset.Id, 0));
        return Project(320, 180, FrameRate.Fps30, [asset], Track("tr_v1", TrackKind.Video, video), Track("tr_a1", TrackKind.Audio, audio));
    }

    /// <summary>A 25 fps 320x180 timeline with the 20 s oracle on V1 (frame k shows value k) and an empty audio track.</summary>
    public async Task<Project> OracleProjectAsync(string file)
    {
        var asset = (await Preview.Importer.ImportAsync(file, CancellationToken.None)).Asset;
        return Project(
            320,
            180,
            FrameRate.Fps25,
            [asset],
            Track("tr_v1", TrackKind.Video, [V("c_o", 0, 500, asset.Id, 0)]),
            Track("tr_a1", TrackKind.Audio, ImmutableArray<Clip>.Empty));
    }

    /// <summary>Waits until a condition holds, failing after <paramref name="timeout"/>.</summary>
    public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string what)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {what}.");
            await Task.Delay(10);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions)
        {
            await session.DisposeAsync();
        }

        await Preview.DisposeAsync();
    }
}
