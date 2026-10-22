using Cutlass.Agent.Api.Requests;
using Cutlass.IntegrationTests.Agent;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Examples;

/// <summary>
/// Runs <c>build/examples/agent-http.ps1</c> (AGENT_INTEGRATION 9.5) against an in-process headless host with the
/// HTTP API: once over a placed clip (the split path) and once over an empty project (the marker path). Each run must
/// exit 0, write a PNG, leave its edit in the project and give the lock back.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class HttpExampleTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public HttpExampleTests(TestMediaFixture media)
    {
        _media = media;
    }

    [PowerShellFact]
    public async Task SplitsTheFirstClipRendersFrameZeroAndReleases()
    {
        await using var kit = await AgentKit.CreateAsync(_media);
        var client = kit.Client;
        await client.AcquireLockAsync("Test setup", "Place a clip");
        var imported = await client.ImportMediaAsync([_media.Cfr25Av]);
        Assert.Empty(imported.Failed);
        var placed = await client.PlaceAsync(new PlaceRequest { MediaId = imported.Imported[0].Id, At = 0, SourceInSeconds = 0, SourceOutSeconds = 4 });
        Assert.True(placed.Changed);
        await client.ReleaseLockAsync();
        var before = (await client.GetProjectAsync()).Tracks.First(t => t.Kind == "video" && t.Clips.Count > 0).Clips[0];

        var (exitCode, output, png) = await RunExampleAsync(kit);

        Assert.True(exitCode == 0, output);
        Assert.Contains("agent-http: split", output, StringComparison.Ordinal);
        Assert.Equal(PngSignature, png[..8]);
        var after = (await client.GetProjectAsync()).Tracks.First(t => t.Kind == "video" && t.Clips.Count > 0).Clips;
        Assert.Equal(2, after.Count);
        Assert.Equal(before.Start + (before.Duration / 2), after[1].Start);
        Assert.Equal("free", (await client.GetLockAsync()).Phase);
    }

    [PowerShellFact]
    public async Task AddsAMarkerWhenThereIsNoClip()
    {
        await using var kit = await AgentKit.CreateAsync(_media);

        var (exitCode, output, png) = await RunExampleAsync(kit);

        Assert.True(exitCode == 0, output);
        Assert.Contains("added a marker", output, StringComparison.Ordinal);
        Assert.Equal(PngSignature, png[..8]);
        Assert.Single((await kit.Client.GetProjectAsync()).Markers);
        Assert.Equal("free", (await kit.Client.GetLockAsync()).Phase);
    }

    private static async Task<(int ExitCode, string Output, byte[] Png)> RunExampleAsync(AgentKit kit)
    {
        var png = kit.Temp.Combine("out", "frame0.png");
        var args = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            args.AddRange(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass"]);
        }

        args.AddRange(["-File", ExampleScripts.PathOf("agent-http.ps1"), "-BaseUrl", kit.Server.BaseUrl!, "-Token", AgentKit.Token, "-OutFile", png]);
        var result = await TestProcess.RunAsync(ExampleScripts.PowerShell!, args, kit.Temp.Path, TimeSpan.FromMinutes(3));
        var bytes = File.Exists(png) ? await File.ReadAllBytesAsync(png) : new byte[8];
        return (result.ExitCode, result.Stdout + result.Stderr, bytes);
    }
}
