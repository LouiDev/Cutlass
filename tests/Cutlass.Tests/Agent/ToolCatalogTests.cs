using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Tests.Agent.Kit;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Agent;

/// <summary>The tool catalog (AGENT_INTEGRATION 5.2 and 7.1).</summary>
public sealed class ToolCatalogTests
{
    private static readonly string[] SpecMcpNames =
    [
        "cutlass_status", "cutlass_acquire_lock", "cutlass_release_lock", "cutlass_get_project", "cutlass_describe_project",
        "cutlass_new_project", "cutlass_open_project", "cutlass_save_project", "cutlass_import_media", "cutlass_list_media",
        "cutlass_render_frame", "cutlass_contact_sheet", "cutlass_audio_levels", "cutlass_find_silences", "cutlass_place_media",
        "cutlass_move_clips", "cutlass_trim_clip", "cutlass_split", "cutlass_delete_clips", "cutlass_remove_ranges", "cutlass_close_gap",
        "cutlass_set_clip_props", "cutlass_set_speed", "cutlass_set_volume_keys", "cutlass_add_transition", "cutlass_update_transition",
        "cutlass_remove_transitions", "cutlass_add_title", "cutlass_update_title", "cutlass_add_marker", "cutlass_update_marker",
        "cutlass_remove_markers", "cutlass_set_in_out", "cutlass_add_track", "cutlass_update_track", "cutlass_remove_track",
        "cutlass_set_sequence", "cutlass_set_playhead", "cutlass_undo", "cutlass_redo", "cutlass_begin_batch", "cutlass_end_batch",
        "cutlass_cancel_batch", "cutlass_start_export", "cutlass_export_status", "cutlass_cancel_export",
    ];

    [Fact]
    public void EveryToolHasAShortDescriptionAndAValidSchema()
    {
        foreach (var tool in ToolCatalog.All)
        {
            Assert.InRange(tool.Description.Length, 10, 299);
            Assert.StartsWith("/v1/", tool.Route, StringComparison.Ordinal);
            using var schema = JsonDocument.Parse(tool.Schema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.DoesNotContain(((char)0x2014).ToString(), tool.Description, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void McpNamesAreUniquePrefixedShortAndCoverTheSpec()
    {
        var names = ToolCatalog.McpTools.Select(t => t.McpName!).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, n =>
        {
            Assert.StartsWith("cutlass_", n, StringComparison.Ordinal);
            Assert.True(n.Length <= 64);
        });
        foreach (var name in SpecMcpNames)
        {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void CatalogNamesAndRoutesAreUnique()
    {
        Assert.Equal(ToolCatalog.All.Length, ToolCatalog.All.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(ToolCatalog.All.Length, ToolCatalog.All.Select(t => t.Method + " " + t.Route).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryCatalogEntryHasAHandlerAndBack()
    {
        using var host = new FakeAgentHost(new ProjectBuilder().Build());
        var handlers = host.Dispatcher.ToolNames.ToHashSet(StringComparer.Ordinal);
        Assert.All(ToolCatalog.All, t => Assert.Contains(t.Name, handlers));
        Assert.Equal(ToolCatalog.All.Length, handlers.Count);
    }

    [Theory]
    [InlineData("GET", "/v1/media/m_1", "media.get", "mediaId", "m_1")]
    [InlineData("DELETE", "/v1/media/m_1", "media.remove", "mediaId", "m_1")]
    [InlineData("GET", "/v1/frame/60", "frame.render", "frame", "60")]
    [InlineData("POST", "/v1/exports/abc/cancel", "exports.cancel", "exportId", "abc")]
    [InlineData("POST", "/v1/edit/title/add", "edit.title.add", null, null)]
    public void RoutesMatchWithTheirValues(string method, string path, string tool, string? key, string? value)
    {
        var info = ToolCatalog.Match(method, path, out var values);
        Assert.NotNull(info);
        Assert.Equal(tool, info.Name);
        if (key is not null)
        {
            Assert.Equal(value, values[key]);
        }

        Assert.Equal(path, ToolCatalog.FillRoute(info, k => values.TryGetValue(k, out var v) ? v : null));
    }

    [Fact]
    public void UnknownRoutesDoNotMatch()
    {
        Assert.Null(ToolCatalog.Match("GET", "/v1/nope", out _));
        Assert.Null(ToolCatalog.Match("PUT", "/v1/status", out _));
    }
}
