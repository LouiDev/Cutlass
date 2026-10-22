using System.Text;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Lock;
using Cutlass.Core.Model;
using Cutlass.Tests.Compiler;
using Cutlass.Tests.TestSupport;
using static Cutlass.Tests.TestSupport.ProjectBuilder;
using static Cutlass.Tests.TestSupport.TestMedia;

namespace Cutlass.Tests.Agent;

/// <summary>
/// The project summary and the describe text against goldens in <c>tests/Cutlass.Tests/Agent/Goldens/</c>
/// (AGENT_INTEGRATION 4.5 and 7.1), under de-DE. <c>CUTLASS_UPDATE_GOLDEN=1</c> rewrites them.
/// </summary>
[UseCulture("de-DE")]
public sealed class ProjectSummaryMapperTests
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static Project Sample()
    {
        var music = A("c_music", 0, 300, media: Music) with { Audio = AudioProps.Default with { GainDb = -6, FadeOutFrames = 12, Keys = [new VolumeKey(90, -3)] } };
        return new ProjectBuilder()
            .With(V1, V("c_01", 0, 150, sourceFrame: 50, link: "l_01"), V("c_05", 150, 300, sourceFrame: 400, speed: 200))
            .With(A1, A("c_11", 0, 150, sourceFrame: 50, link: "l_01"))
            .With(A2, music)
            .With(V2, Title("c_02", 0, 120, "Meet Ana"))
            .Transition(V1, "x_01", "c_01", "c_05", 30)
            .Marker("k_3m9q0aa1", 300, "Drop")
            .InOut(10, null)
            .Build();
    }

    private static void Match(string fileName, string actual)
    {
        var dir = Path.Combine(Golden.RepositoryRoot(), "tests", "Cutlass.Tests", "Agent", "Goldens");
        var path = Path.Combine(dir, fileName);
        if (Golden.Updating)
        {
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, Utf8NoBom.GetBytes(actual));
            return;
        }

        Assert.True(File.Exists(path), $"Golden file {fileName} is missing. Run with CUTLASS_UPDATE_GOLDEN=1. Actual:\n{actual}");
        Assert.Equal(Utf8NoBom.GetString(File.ReadAllBytes(path)).ReplaceLineEndings("\n"), actual);
    }

    [Fact]
    public void TheSummaryMatchesTheGolden()
    {
        var dto = ProjectSummaryMapper.Map(Sample(), "D:\\Edits\\interview.cutlass", true, 57, 120, id => id != Short, null);
        Match("summary.json", JsonSerializer.Serialize(dto, AgentJsonContext.Indented.ProjectSummaryDto).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void TheDescribeTextMatchesTheGolden()
    {
        var state = AgentLockState.Free with { Phase = LockPhase.Held, AgentName = "Claude Code", Reason = "Remove silences" };
        var text = ProjectDescriber.Describe(Sample(), "D:/Edits/interview.cutlass", true, 57, 120, state, id => id != Short);
        Match("describe.txt", text);
        Assert.Contains("Lock: Claude Code (Remove silences)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TracksAreListedTopToBottomWithKindIndexes()
    {
        var dto = ProjectSummaryMapper.Map(Sample(), null, false, 1, 0, _ => true, null);
        Assert.Equal([V2, V1, A1, A2], dto.Tracks.Select(t => t.Id));
        Assert.Equal([2, 1, 1, 2], dto.Tracks.Select(t => t.Index));
        Assert.Equal("Untitled", dto.Name);
        Assert.Null(dto.Tracks[0].Muted);
        Assert.Equal(false, dto.Tracks[2].Muted);
    }

    [Fact]
    public void SourceOutFlicksIncludesTheSpeed()
    {
        var dto = ProjectSummaryMapper.Map(Sample(), null, false, 1, 0, _ => true, null);
        var normal = dto.Tracks.Single(t => t.Id == V1).Clips.Single(c => c.Id == "c_01");
        var fast = dto.Tracks.Single(t => t.Id == V1).Clips.Single(c => c.Id == "c_05");
        Assert.Equal((50 + 150) * F, normal.SourceOutFlicks);
        Assert.Equal((400 + (300 * 2)) * F, fast.SourceOutFlicks);
        Assert.Equal(16.0, fast.SourceInSeconds);
        Assert.Equal(40.0, fast.SourceOutSeconds);
        Assert.Equal(200, fast.SpeedPercent);
    }

    [Fact]
    public void FiltersKeepTheRequestedTracksAndFrames()
    {
        var dto = ProjectSummaryMapper.Map(Sample(), null, false, 1, 0, _ => true, new ProjectQueryRequest { TrackIds = [V1], From = 160, To = 200 });
        var track = Assert.Single(dto.Tracks);
        Assert.Equal(["c_05"], track.Clips.Select(c => c.Id));
        Assert.Equal(["x_01"], track.Transitions.Select(t => t.Id));
        Assert.Empty(dto.Markers);
    }

    [Fact]
    public void TheDescribeTextStaysUnderItsLimit()
    {
        var clips = Enumerable.Range(0, 20_000).Select(i => (Clip)V("c_" + i, i * 2L, 2)).ToArray();
        var p = new ProjectBuilder().With(V1, clips).Build();
        var text = ProjectDescriber.Describe(p, null, false, 1, 0, AgentLockState.Free, _ => true);
        Assert.True(text.Length <= ProjectDescriber.MaxLength);
        Assert.EndsWith("from and to)\n", text, StringComparison.Ordinal);
    }
}
