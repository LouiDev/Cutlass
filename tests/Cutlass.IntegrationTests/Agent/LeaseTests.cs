using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto;
using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Lock;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>Lease expiry and lock loss in real time through the headless host (AGENT_INTEGRATION 3.3, 3.5 and 7.2).</summary>
[Collection(TestMediaCollection.Name)]
public sealed class LeaseTests
{
    private readonly TestMediaFixture _media;

    public LeaseTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task AShortLeaseExpiresAndTheOpenBatchIsCommitted()
    {
        await using var kit = await AgentKit.CreateAsync(_media, LeaseOptions.Default with { MinLeaseSeconds = 1 });
        var client = kit.Client;
        var grant = await client.AcquireLockAsync("Integration", "Lease test", leaseSeconds: 2);
        Assert.Equal(2, grant.LeaseSeconds);
        await client.BeginBatchAsync("Markers");
        await client.AddMarkerAsync(new MarkerAddRequest { Frame = 10, Name = "A" });
        await client.AddMarkerAsync(new MarkerAddRequest { Frame = 20, Name = "B" });

        // The driver ticks every second: the lease is gone a little after 2 s of silence.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        LockStateDto? state = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            using var plain = new Cutlass.Agent.Http.AgentClient(client.BaseUrl, AgentKit.Token);
            state = await plain.GetLockAsync();
            if (state.Phase == "free")
            {
                break;
            }
        }

        Assert.Equal("free", state!.Phase);
        var lost = await client.CallAsync("edit.marker.add", JsonDocument.Parse("""{"frame":30}""").RootElement);
        Assert.Equal(ErrorCode.LockLost, lost.Error!.Code);
        Assert.Equal("expired", lost.Error.Detail("reason"));
        var (inGroup, undoLabel, markers) = await kit.Host.Ui.InvokeAsync(
            () => (kit.Host.Session.InGroup, kit.Host.Session.UndoLabel, kit.Host.Session.Current.Sequence.Markers.Length),
            CancellationToken.None);
        Assert.False(inGroup);
        Assert.Equal("Agent: Markers", undoLabel);
        Assert.Equal(2, markers);
    }

    [FfmpegFact]
    public async Task TakeOverRevokesTheLockAndTheAgentIsTold()
    {
        await using var kit = await AgentKit.CreateAsync(_media);
        var client = kit.Client;
        await client.AcquireLockAsync("Integration", "Revoke test");
        await client.BeginBatchAsync("Work");
        await client.AddMarkerAsync(new MarkerAddRequest { Frame = 10 });
        await kit.Host.Ui.InvokeAsync(() => kit.Host.Lock.Revoke(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), CancellationToken.None);
        var lost = await client.CallAsync("edit.marker.add", JsonDocument.Parse("""{"frame":30}""").RootElement);
        Assert.Equal(ErrorCode.LockLost, lost.Error!.Code);
        Assert.Equal("revoked_by_user", lost.Error.Detail("reason"));

        // The user can undo the committed batch at once.
        var undone = await kit.Host.Ui.InvokeAsync(() => kit.Host.Session.Undo(), CancellationToken.None);
        Assert.True(undone);
    }
}
