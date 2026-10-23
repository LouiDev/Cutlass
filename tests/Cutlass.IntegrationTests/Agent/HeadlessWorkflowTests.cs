using System.Globalization;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Agent.Api.Dto.Project;
using Cutlass.Agent.Api.Requests;
using Cutlass.Agent.Api.Requests.Clips;
using Cutlass.Agent.Api.Requests.Media;
using Cutlass.Agent.Api.Requests.Preview;
using Cutlass.Agent.Api.Requests.Project;
using Cutlass.Agent.Api.Requests.Titles;
using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>
/// The headless host and the HTTP API end to end over the fixture media (AGENT_INTEGRATION 7.2): status and acquire,
/// import (and a refused import), place and split, a batch undone as one step, frame and contact sheet renders checked
/// against the oracle, an H.264 export, save and reopen, and release.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class HeadlessWorkflowTests
{
    private readonly TestMediaFixture _media;

    public HeadlessWorkflowTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task AnAgentEditsRendersExportsAndSaves()
    {
        await using var kit = await AgentKit.CreateAsync(_media);
        var client = kit.Client;

        // 1. Status and acquire.
        var status = await client.StatusAsync();
        Assert.True(status.Ffmpeg.Ready, status.Ffmpeg.Reason);
        Assert.Equal("headless", status.HostKind);
        Assert.Equal("free", status.Lock.Phase);
        var grant = await client.AcquireLockAsync("Integration", "Workflow test");
        Assert.Equal("held", grant.Phase);
        var project = await client.NewProjectAsync(new NewProjectRequest { Width = 320, Height = 180, FrameRate = "25" });
        Assert.Equal("25/1", project.Sequence.FrameRate);

        // 2. Import, and an import outside the read folders.
        var imported = await client.ImportMediaAsync([_media.Oracle25Intra, _media.Cfr25Av]);
        Assert.Empty(imported.Failed);
        Assert.Equal(2, imported.Imported.Count);
        var oracle = imported.Imported[0];
        var av = imported.Imported[1];
        Assert.Equal(320, oracle.Video!.Width);
        var outside = Path.Combine(kit.Temp.Path, "elsewhere.wav");
        await File.WriteAllBytesAsync(outside, [0]);
        var refused = await client.CallAsync("media.import", JsonDocument.Parse(JsonSerializer.Serialize(new ImportRequest { Paths = [outside] }, AgentJsonContext.Default.ImportRequest)).RootElement);
        Assert.Equal(ErrorCode.Forbidden, refused.Error!.Code);
        Assert.Equal(outside, refused.Error.Detail("path"));

        // 3. Place and split.
        var placed = await client.PlaceAsync(new PlaceRequest { MediaId = oracle.Id, At = 0, SourceInSeconds = 0, SourceOutSeconds = 4 });
        Assert.True(placed.Changed);
        Assert.Equal("Agent: Overwrite", placed.UndoLabel);
        await client.PlaceAsync(new PlaceRequest { MediaId = av.Id, At = 100 });
        var split = await client.SplitAsync(new SplitRequest { Frame = 50 });
        Assert.True(split.Changed);
        var afterSplit = await client.GetProjectAsync();
        var v1 = afterSplit.Tracks.Single(t => t.Id == "tr_v1" || (t.Kind == "video" && t.Index == 1));
        Assert.Equal([0L, 50L, 100L], v1.Clips.Select(c => c.Start));
        Assert.Equal(200, afterSplit.Sequence.DurationFrames);

        // 4. A batch with a move and a title, undone as one step.
        var beforeBatch = JsonSerializer.Serialize(afterSplit.Tracks, AgentJsonContext.Default.ListTrackSummaryDto);
        await client.BeginBatchAsync("Tidy");
        await client.MoveAsync(new MoveRequest { ClipIds = [v1.Clips[2].Id], DeltaFrames = 10 });
        var v2 = afterSplit.Tracks.Single(t => t.Kind == "video" && t.Index == 2);
        await client.AddTitleAsync(new TitleAddRequest { TrackId = v2.Id, At = 0, DurationFrames = 25, Title = new TitleDto { Text = "Hello" } });
        var end = await client.EndBatchAsync();
        Assert.Equal("Agent: Tidy", end.UndoLabel);
        await client.UndoAsync();
        var afterUndo = await client.GetProjectAsync();
        Assert.Equal(beforeBatch, JsonSerializer.Serialize(afterUndo.Tracks, AgentJsonContext.Default.ListTrackSummaryDto));
        await client.RedoAsync();
        await client.UndoAsync();

        // 5. A frame render decoded with ffmpeg shows the oracle frame; so do the contact sheet cells.
        var png = await client.RenderFrameAsync(30, 320);
        var (w, h, bgra) = await AgentKit.DecodePngAsync(png, kit.OutputDirectory, 320, 180);
        Assert.Equal(TestMediaFixture.ExpectedOracleValue(30), OracleReader.DecodeBgra(bgra, w, h));
        var (sheet, layout) = await client.ContactSheetAsync(new ContactSheetRequest { Frames = [5, 17, 40], CellWidth = 320, Columns = 2 });
        var sheetSize = (Width: 2 * 320 + 2, Height: 2 * 180 + 2);
        var (_, _, sheetPixels) = await AgentKit.DecodePngAsync(sheet, kit.OutputDirectory, sheetSize.Width, sheetSize.Height);
        Assert.Equal(3, layout.Count);
        foreach (var cell in layout)
        {
            var offset = (cell.Y * sheetSize.Width * 4) + (cell.X * 4);
            var value = OracleReader.DecodeBgra(sheetPixels.AsSpan(offset), 320, 180, sheetSize.Width * 4);
            Assert.Equal(TestMediaFixture.ExpectedOracleValue(cell.Frame), value);
        }

        // 6. An H.264 export with the ffprobe frame count.
        var output = Path.Combine(kit.OutputDirectory, "agent.mp4");
        var export = await client.StartExportAsync(new ExportStartRequest { Preset = "h264", Quality = "good", OutputPath = output });
        var done = await client.WaitForExportAsync(export.Id, TimeSpan.FromMinutes(3));
        Assert.True(done.State == "completed", done.Error + " " + string.Join(" | ", done.LogTail));
        var probe = await TestProcess.RunAsync(
            TestFfmpeg.FfprobePath,
            ["-v", "error", "-select_streams", "v:0", "-count_packets", "-show_entries", "stream=nb_read_packets", "-of", "csv=p=0", output],
            kit.OutputDirectory,
            TimeSpan.FromMinutes(1));
        Assert.Equal(200, int.Parse(probe.Stdout.Trim(), CultureInfo.InvariantCulture));
        Assert.Contains(await client.ListExportsAsync(), e => e.Id == export.Id);

        // 7. Save, reopen and compare.
        var saved = await client.SaveProjectAsAsync(new SaveAsRequest { Path = Path.Combine(kit.OutputDirectory, "edit.cutlass") });
        Assert.EndsWith("edit.cutlass", saved.Path, StringComparison.Ordinal);
        var before = await client.GetProjectAsync();
        Assert.False(before.Dirty);
        var reopened = await client.OpenProjectAsync(new OpenProjectRequest { Path = saved.Path });
        Assert.Equal(
            JsonSerializer.Serialize(before.Tracks, AgentJsonContext.Default.ListTrackSummaryDto),
            JsonSerializer.Serialize(reopened.Tracks, AgentJsonContext.Default.ListTrackSummaryDto));
        Assert.Equal(before.Media.Select(m => m.Id), reopened.Media.Select(m => m.Id));

        // 8. Release; mutating calls then need the lock again.
        await client.ReleaseLockAsync();
        var after = await client.CallAsync("edit.split", JsonDocument.Parse("""{"frame":10}""").RootElement);
        Assert.Equal(ErrorCode.LockRequired, after.Error!.Code);
        Assert.Equal("user", after.Error.Detail("holder"));
    }
}
