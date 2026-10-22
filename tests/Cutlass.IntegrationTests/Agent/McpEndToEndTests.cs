using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.IntegrationTests.Infrastructure;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>
/// The MCP server end to end (AGENT_INTEGRATION 7.2): launches the built <c>Cutlass.Agent.Mcp</c> with
/// <c>dotnet</c> in <c>--headless</c> mode over the fixture media and drives it with the SDK client over stdio.
/// </summary>
[Collection(TestMediaCollection.Name)]
public sealed class McpEndToEndTests
{
    /// <summary>The tool names of AGENT_INTEGRATION 5.2, which must all be listed.</summary>
    private static readonly string[] SpecToolNames =
    [
        "cutlass_status", "cutlass_acquire_lock", "cutlass_release_lock", "cutlass_get_project", "cutlass_describe_project",
        "cutlass_new_project", "cutlass_open_project", "cutlass_save_project", "cutlass_import_media", "cutlass_list_media",
        "cutlass_render_frame", "cutlass_contact_sheet", "cutlass_audio_levels", "cutlass_find_silences", "cutlass_place_media",
        "cutlass_move_clips", "cutlass_trim_clip", "cutlass_split", "cutlass_delete_clips", "cutlass_remove_ranges",
        "cutlass_close_gap", "cutlass_set_clip_props", "cutlass_set_speed", "cutlass_set_volume_keys", "cutlass_add_transition",
        "cutlass_update_transition", "cutlass_remove_transitions", "cutlass_add_title", "cutlass_update_title",
        "cutlass_add_marker", "cutlass_update_marker", "cutlass_remove_markers", "cutlass_set_in_out", "cutlass_add_track",
        "cutlass_update_track", "cutlass_remove_track", "cutlass_set_sequence", "cutlass_set_playhead", "cutlass_undo",
        "cutlass_redo", "cutlass_begin_batch", "cutlass_end_batch", "cutlass_cancel_batch", "cutlass_start_export",
        "cutlass_export_status", "cutlass_cancel_export",
    ];

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly TestMediaFixture _media;

    /// <summary>Receives the shared media fixture.</summary>
    /// <param name="media">The fixture.</param>
    public McpEndToEndTests(TestMediaFixture media)
    {
        _media = media;
    }

    [FfmpegFact]
    public async Task HeadlessServerListsEveryToolAndEditsUnderTheLock()
    {
        using var temp = new TempDirectory("cutlass-mcp-e2e");
        var project = temp.Combine("e2e.cutlass");
        var ffmpegBefore = FfmpegPids();
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "cutlass",
            Command = "dotnet",
            Arguments =
            [
                McpDllPath(), "--headless", "--project", project, "--create",
                "--allow-read", _media.Directory, "--allow-write", temp.Path,
            ],
            WorkingDirectory = temp.Path,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                // The headless engine searches the PATH; put the test ffmpeg first.
                ["PATH"] = Path.GetDirectoryName(TestFfmpeg.FfmpegPath) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            },
            ShutdownTimeout = TimeSpan.FromSeconds(30),
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var ct = timeout.Token;
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var disposed = false;
        try
        {
            Assert.Equal("cutlass", client.ServerInfo?.Name);
            Assert.Contains("cutlass_acquire_lock", client.ServerInstructions ?? string.Empty, StringComparison.Ordinal);

            // Every catalog tool MCP exposes, and every name of section 5.2, is listed.
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var info in ToolCatalog.McpTools)
            {
                Assert.Contains(info.McpName!, names);
            }

            foreach (var name in SpecToolNames)
            {
                Assert.Contains(name, names);
            }

            Assert.Equal(ToolCatalog.McpTools.Count(), tools.Count);
            var acquireSchema = tools.Single(t => t.Name == "cutlass_acquire_lock").JsonSchema;
            Assert.True(acquireSchema.GetProperty("properties").TryGetProperty("waitSeconds", out _));

            // The resources and the workflow prompt.
            var resources = await client.ListResourcesAsync(cancellationToken: ct);
            Assert.Contains(resources, r => r.Uri == "cutlass://project");
            var templates = await client.ListResourceTemplatesAsync(cancellationToken: ct);
            Assert.Contains(templates, t => t.UriTemplate == "cutlass://frame/{n}");
            var prompt = await client.GetPromptAsync("cutlass_workflow", cancellationToken: ct);
            Assert.Contains("cutlass_release_lock", Assert.IsType<TextContentBlock>(prompt.Messages.Single().Content).Text, StringComparison.Ordinal);

            // Status first: the text is the JSON envelope.
            var status = await CallJsonAsync(client, "cutlass_status", new Dictionary<string, object?>(), ct);
            Assert.Equal("headless", status.GetProperty("result").GetProperty("hostKind").GetString());

            // A mutating call without the lock is a lock_required tool error.
            await AssertLockRequiredAsync(client, ct);

            await CallJsonAsync(client, "cutlass_acquire_lock", new Dictionary<string, object?> { ["agentName"] = "McpEndToEndTests", ["reason"] = "End to end test", ["waitSeconds"] = 1 }, ct);

            var import = await CallJsonAsync(client, "cutlass_import_media", new Dictionary<string, object?> { ["paths"] = new[] { _media.Cfr25Av } }, ct);
            var mediaId = import.GetProperty("result").GetProperty("imported")[0].GetProperty("id").GetString();
            Assert.False(string.IsNullOrEmpty(mediaId));

            var place = await CallJsonAsync(client, "cutlass_place_media", new Dictionary<string, object?> { ["mediaId"] = mediaId, ["at"] = 0 }, ct);
            Assert.True(place.GetProperty("changed").GetBoolean());

            // Frame 0 comes back as a PNG image block next to a JSON text block.
            var frame = await client.CallToolAsync("cutlass_render_frame", new Dictionary<string, object?> { ["frame"] = 0, ["width"] = 320 }, cancellationToken: ct);
            Assert.NotEqual(true, frame.IsError);
            var image = Assert.Single(frame.Content.OfType<ImageContentBlock>());
            Assert.Equal("image/png", image.MimeType);
            var (width, height) = DecodePng(image.DecodedData.ToArray());
            Assert.Equal(320, width);
            Assert.True(height > 0);
            var meta = JsonDocument.Parse(Assert.Single(frame.Content.OfType<TextContentBlock>()).Text).RootElement;
            Assert.Equal(0, meta.GetProperty("result").GetProperty("frame").GetInt64());

            // Save, so the headless host leaves no autosave behind, then give the project back.
            await CallJsonAsync(client, "cutlass_save_project", new Dictionary<string, object?>(), ct);
            await CallJsonAsync(client, "cutlass_release_lock", new Dictionary<string, object?>(), ct);
            await AssertLockRequiredAsync(client, ct);
            Assert.True(File.Exists(project));
        }
        finally
        {
            var watch = Stopwatch.StartNew();
            await client.DisposeAsync();
            disposed = true;
            var details = await client.Completion;
            if (details is StdioClientCompletionDetails stdio)
            {
                // The server notices stdin closing and exits by itself, well inside the client's kill timeout.
                Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"The MCP server took {watch.Elapsed} to exit.");
                Assert.Equal(0, stdio.ExitCode);
                if (stdio.ProcessId is { } pid)
                {
                    AssertGone(pid);
                }
            }
        }

        Assert.True(disposed);
        await AssertNoNewFfmpegAsync(ffmpegBefore);
    }

    private static async Task AssertLockRequiredAsync(McpClient client, CancellationToken ct)
    {
        var result = await client.CallToolAsync("cutlass_add_marker", new Dictionary<string, object?> { ["frame"] = 0, ["name"] = "x" }, cancellationToken: ct);
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.StartsWith("lock_required: ", text, StringComparison.Ordinal);
    }

    private static async Task<JsonElement> CallJsonAsync(McpClient client, string tool, Dictionary<string, object?> args, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, args, cancellationToken: ct);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        Assert.True(result.IsError != true, $"{tool} failed: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>The built MCP dll in the same configuration as this test assembly.</summary>
    internal static string McpDllPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cutlass.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var dll = Path.Combine(dir.FullName, "src", "Cutlass.Agent.Mcp", "bin", configuration, "net10.0", "Cutlass.Agent.Mcp.dll");
        Assert.True(File.Exists(dll), $"Build src/Cutlass.Agent.Mcp first; {dll} is missing.");
        return dll;
    }

    /// <summary>
    /// Checks a PNG: the signature, IHDR, and IDAT data that inflates to exactly one filter byte plus one row of
    /// pixels per line. Returns the size.
    /// </summary>
    private static (int Width, int Height) DecodePng(byte[] png)
    {
        Assert.True(png.AsSpan().StartsWith(PngSignature), "The image does not start with the PNG signature.");
        int width = 0, height = 0, bytesPerPixel = 0;
        using var idat = new MemoryStream();
        var offset = PngSignature.Length;
        var sawEnd = false;
        while (offset + 8 <= png.Length && !sawEnd)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                    Assert.Equal(8, data[8]);
                    bytesPerPixel = data[9] switch { 2 => 3, 6 => 4, 0 => 1, _ => 0 };
                    Assert.NotEqual(0, bytesPerPixel);
                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
                case "IEND":
                    sawEnd = true;
                    break;
            }

            offset += 12 + length;
        }

        Assert.True(sawEnd, "The PNG has no IEND chunk.");
        idat.Position = 0;
        using var inflated = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            z.CopyTo(inflated);
        }

        Assert.Equal((long)height * (1 + ((long)width * bytesPerPixel)), inflated.Length);
        return (width, height);
    }

    private static HashSet<int> FfmpegPids() =>
        [.. Process.GetProcessesByName("ffmpeg").Select(p =>
        {
            using (p)
            {
                return p.Id;
            }
        })];

    private static async Task AssertNoNewFfmpegAsync(HashSet<int> before)
    {
        // The server's ffmpeg children die with it (Job Object or pid registry). Other test collections may run
        // short ffmpeg jobs at the same time, so wait a little for those before failing.
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var left = FfmpegPids().Except(before).ToList();
            if (left.Count == 0)
            {
                return;
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"ffmpeg processes left: {string.Join(", ", left)}.");
            await Task.Delay(100);
        }
    }

    private static void AssertGone(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            Assert.True(process.HasExited, $"The MCP server (pid {pid}) is still running.");
        }
        catch (ArgumentException)
        {
            // Gone.
        }
    }
}
