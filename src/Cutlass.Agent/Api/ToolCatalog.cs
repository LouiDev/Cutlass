using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Cutlass.Agent.Api;

/// <summary>
/// Every tool of the agent API (AGENT_INTEGRATION 2.1, 4.4 and 5.2): catalog name, MCP name, route, lock and result
/// kind, description and argument schema. The HTTP router, <c>/v1/call</c>, the MCP server and the tests all read it.
/// Route segments in braces (<c>{mediaId}</c>) are arguments of that name; query parameters are arguments too.
/// </summary>
public static class ToolCatalog
{
    private static readonly ImmutableArray<ToolInfo> Tools = Build();
    private static readonly FrozenDictionary<string, ToolInfo> ByName = Tools.ToFrozenDictionary(t => t.Name, StringComparer.Ordinal);
    private static readonly FrozenDictionary<string, ToolInfo> ByMcp =
        Tools.Where(t => t.McpName is not null).ToFrozenDictionary(t => t.McpName!, StringComparer.Ordinal);

    /// <summary>Every tool, in a stable order.</summary>
    public static ImmutableArray<ToolInfo> All => Tools;

    /// <summary>The tools MCP exposes.</summary>
    public static IEnumerable<ToolInfo> McpTools => Tools.Where(t => t.McpName is not null);

    /// <summary>Finds a tool by catalog name.</summary>
    /// <param name="name">For example <c>edit.split</c>.</param>
    /// <returns>The tool, or null.</returns>
    public static ToolInfo? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>Finds a tool by MCP name.</summary>
    /// <param name="mcpName">For example <c>cutlass_split</c>.</param>
    /// <returns>The tool, or null.</returns>
    public static ToolInfo? FindByMcpName(string mcpName) => ByMcp.GetValueOrDefault(mcpName);

    /// <summary>Matches a request to a tool and extracts the route arguments.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path without query, for example <c>/v1/media/m_1/peaks</c>.</param>
    /// <param name="routeValues">The values of the braced segments.</param>
    /// <returns>The tool, or null when no route matches.</returns>
    public static ToolInfo? Match(string method, string path, out IReadOnlyDictionary<string, string> routeValues)
    {
        var parts = path.Trim('/').Split('/');
        foreach (var tool in Tools)
        {
            if (!string.Equals(tool.Method, method, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var template = tool.Route.Trim('/').Split('/');
            if (template.Length != parts.Length)
            {
                continue;
            }

            Dictionary<string, string>? values = null;
            var ok = true;
            for (var i = 0; i < template.Length && ok; i++)
            {
                var t = template[i];
                if (t.StartsWith('{') && t.EndsWith('}'))
                {
                    values ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    values[t[1..^1]] = Uri.UnescapeDataString(parts[i]);
                }
                else
                {
                    ok = string.Equals(t, parts[i], StringComparison.Ordinal);
                }
            }

            if (ok)
            {
                routeValues = values ?? (IReadOnlyDictionary<string, string>)FrozenDictionary<string, string>.Empty;
                return tool;
            }
        }

        routeValues = FrozenDictionary<string, string>.Empty;
        return null;
    }

    /// <summary>The route of a tool with its braced segments filled from <paramref name="values"/>.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="values">Looks up a segment's value by name, or returns null.</param>
    /// <returns>The path.</returns>
    /// <exception cref="ArgumentException">When a segment has no value.</exception>
    public static string FillRoute(ToolInfo tool, Func<string, string?> values)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(values);
        var parts = tool.Route.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].StartsWith('{') && parts[i].EndsWith('}'))
            {
                var name = parts[i][1..^1];
                parts[i] = Uri.EscapeDataString(values(name) ?? throw new ArgumentException($"The route of {tool.Name} needs '{name}'.", nameof(values)));
            }
        }

        return string.Join('/', parts);
    }

    private static SchemaBuilder S() => new();

    private static ImmutableArray<ToolInfo> Build()
    {
        var list = new List<ToolInfo>();

        void Read(string name, string? mcp, string method, string route, string description, SchemaBuilder schema, bool heavy = false, ToolResultKind result = ToolResultKind.Json) =>
            list.Add(new ToolInfo(name, mcp, method, route, false, false, heavy, result, description, schema.Build()));

        void Locked(string name, string? mcp, string method, string route, string description, SchemaBuilder schema) =>
            list.Add(new ToolInfo(name, mcp, method, route, true, false, false, ToolResultKind.Json, description, schema.Build()));

        void Edit(string name, string? mcp, string route, string description, SchemaBuilder schema, string method = "POST") =>
            list.Add(new ToolInfo(
                name,
                mcp,
                method,
                route,
                true,
                true,
                false,
                ToolResultKind.Json,
                description,
                schema
                    .Int("ifRevision", "Fail with conflict when the project revision is no longer this.")
                    .Bool("returnProject", "Also return the project summary.")
                    .Build()));

        var range = S().Int("start", "First frame.", true, 0).Int("end", "End frame (exclusive).", true, 0);

        // Status, lock, events.
        Read("status", "cutlass_status", "GET", "/v1/status", "State of Cutlass: project, ffmpeg, who holds the edit lock. Call first.", S());
        Read("capabilities", "cutlass_capabilities", "GET", "/v1/capabilities", "Export presets, qualities, transition kinds, title anchors, edit modes and limits of this build and ffmpeg.", S());
        Read("lock.acquire", "cutlass_acquire_lock", "POST", "/v1/lock/acquire", "Take the edit lock before any change. The user sees your name and reason.",
            S().Str("agentName", "Your name as the user should see it.", true)
                .Str("reason", "What you are about to do, in a few words.", true)
                .Int("waitMs", "Wait this long (at most 120000) when the user or another agent is busy.", false, 0, 120000)
                .Int("leaseSeconds", "Lease length, 15 to the configured maximum; renewed by every call you make.", false, 15, 600));
        Locked("lock.renew", "cutlass_renew_lock", "POST", "/v1/lock/renew", "Renew your lease during a long pause. Every other call renews it as well.", S());
        Locked("lock.release", "cutlass_release_lock", "POST", "/v1/lock/release", "Give the project back to the user. Always call when done.", S());
        Read("lock.get", "cutlass_get_lock", "GET", "/v1/lock", "Who holds the edit lock, its phase and lease.", S());
        Read("events", null, "GET", "/v1/events", "Long-poll for project, lock, export and playhead events after a sequence number.",
            S().Int("after", "Return events after this sequence number.", false, 0).Int("timeoutMs", "Wait at most this long (25000 max).", false, 0, 25000));
        Locked("shutdown", null, "POST", "/v1/shutdown", "Stops a headless serve process.", S().Bool("save", "Save the project first."));

        // Project and media.
        Read("project.get", "cutlass_get_project", "GET", "/v1/project", "Timeline as JSON: tracks, clips (ids, frames, source times), transitions, markers, media.",
            S().StrArray("trackIds", "Only these tracks.").Int("from", "Only clips ending after this frame.", false, 0).Int("to", "Only clips starting before this frame.", false, 0));
        Read("project.describe", "cutlass_describe_project", "GET", "/v1/project/describe", "Short text overview of the timeline; cheaper than get_project.",
            S().Int("from", "Only clips ending after this frame.", false, 0).Int("to", "Only clips starting before this frame.", false, 0), result: ToolResultKind.Text);
        Locked("project.new", "cutlass_new_project", "POST", "/v1/project/new", "Create a new untitled project (V1, V2, A1, A2).",
            S().Int("width", "Canvas width (even).", false, 16, 7680).Int("height", "Canvas height (even).", false, 16, 4320)
                .Str("frameRate", "Frame rate such as 30000/1001, 25 or 30.").Bool("discardChanges", "Headless only: drop unsaved changes."));
        Locked("project.open", "cutlass_open_project", "POST", "/v1/project/open", "Open a project file inside the allowed folders.",
            S().Str("path", "The .cutlass file.", true).Bool("discardChanges", "Headless only: drop unsaved changes."));
        Locked("project.save", "cutlass_save_project", "POST", "/v1/project/save", "Save the project to its current path (an untitled project needs save-as).", S());
        Locked("project.save-as", null, "POST", "/v1/project/save-as", "Save the project to a new path inside the write folders.",
            S().Str("path", "The new .cutlass path.", true).Bool("overwrite", "Replace an existing file."));
        Read("media.list", "cutlass_list_media", "GET", "/v1/media", "List the media in the bin with duration, codecs and online state.", S());
        Read("media.get", "cutlass_get_media", "GET", "/v1/media/{mediaId}", "One media asset with probe info and online state.", S().Str("mediaId", "The media id.", true));
        Locked("media.import", "cutlass_import_media", "POST", "/v1/media/import", "Add files to the bin (allowed folders only); place them with place_media.",
            S().StrArray("paths", "Absolute file paths.", true, 32));
        Edit("media.remove", "cutlass_remove_media", "/v1/media/{mediaId}", "Remove a media asset and every clip that uses it.", S().Str("mediaId", "The media id.", true), "DELETE");
        Read("media.peaks", null, "GET", "/v1/media/{mediaId}/peaks", "Source waveform peaks of a media asset.",
            S().Str("mediaId", "The media id.", true).Int("startFlicks", "Source start.").Int("endFlicks", "Source end.").Int("bins", "Number of bins (at most 4000).", false, 1, 4000));
        Read("media.levels", "cutlass_source_levels", "POST", "/v1/media/{mediaId}/levels", "Loudness of a media file's own audio (RMS and peak dBFS per interval).",
            S().Str("mediaId", "The media id.", true).Int("startFlicks", "Source start in flicks.").Num("startSeconds", "Source start in seconds.")
                .Int("endFlicks", "Source end in flicks.").Num("endSeconds", "Source end in seconds.").Int("intervalMs", "Interval, 10 to 1000 (50).", false, 10, 1000),
            heavy: true);

        // Preview and analysis.
        Read("frame.render", "cutlass_render_frame", "GET", "/v1/frame/{frame}", "Look at one timeline frame. Returns an image.",
            S().Int("frame", "Timeline frame.", true, 0).Int("width", "Image width, 16 to 1920, even (640).", false, 16, 1920)
                .Bool("useProxies", "Render from proxies.").Str("format", "png or base64.", false, "png", "base64"),
            heavy: true, result: ToolResultKind.Png);
        Read("contact-sheet", "cutlass_contact_sheet", "POST", "/v1/contact-sheet", "Many frames tiled in one image, with a cell legend.",
            S().IntArray("frames", "Frames to show (at most 64).", false, 64).Int("start", "First frame of an even spread.").Int("end", "End frame of the spread.")
                .Int("count", "Cells of the spread (at most 64).", false, 1, 64).Int("cellWidth", "Cell width, 16 to 1920 (320).", false, 16, 1920)
                .Int("columns", "Columns (default: a near square grid).", false, 1, 64).Str("format", "png or base64.", false, "png", "base64"),
            heavy: true, result: ToolResultKind.Png);
        Read("audio.levels", "cutlass_audio_levels", "POST", "/v1/audio/levels", "Loudness (RMS and peak dBFS) of the mix per interval.",
            S().Int("start", "First frame.", true, 0).Int("end", "End frame (exclusive).", true, 0).Int("intervalMs", "Interval, 10 to 1000 (50).", false, 10, 1000)
                .StrArray("trackIds", "Only these audio tracks (soloed in a snapshot)."),
            heavy: true);
        Read("audio.silences", "cutlass_find_silences", "POST", "/v1/audio/silences", "Frame ranges quieter than the threshold.",
            S().Int("start", "First frame (0).", false, 0).Int("end", "End frame (the sequence end).", false, 0).Num("thresholdDb", "Threshold in dBFS (-40).")
                .Int("minDurationMs", "Shortest silence (500).", false, 0).Int("padMs", "Keep this much sound on each side (100).", false, 0)
                .StrArray("trackIds", "Only these audio tracks."),
            heavy: true);
        Locked("playhead.set", "cutlass_set_playhead", "POST", "/v1/playhead", "Show the user a frame in the viewer.", S().Int("frame", "Timeline frame.", true, 0));

        // Edits.
        var streams = new[] { "both", "video", "audio" };
        var modes = new[] { "overwrite", "insert" };
        Edit("edit.place", "cutlass_place_media", "/v1/edit/place", "Put media, or a source range of it, on the timeline.",
            S().Str("mediaId", "The media id.", true).Int("at", "Timeline frame.", true, 0).Str("mode", "overwrite (default) or insert.", false, modes)
                .Str("streams", "both (default), video or audio.", false, streams).Str("videoTrackId", "Video track (default: targeted).").Str("audioTrackId", "Audio track (default: targeted).")
                .Int("sourceInFlicks", "Source in point.").Num("sourceInSeconds", "Source in point in seconds.").Int("sourceOutFlicks", "Source out point.")
                .Num("sourceOutSeconds", "Source out point in seconds.").Int("durationFrames", "Length in frames (stills, or instead of an out point).", false, 1));
        Edit("edit.move", "cutlass_move_clips", "/v1/edit/move", "Move clips in time and between tracks.",
            S().StrArray("clipIds", "Clip ids.", true, 1000).Int("deltaFrames", "Frames to move (negative: left).", true).Int("deltaTracks", "Tracks to move (positive: up for video).")
                .Str("mode", "overwrite (default) or insert.", false, modes).Bool("includeLinked", "Move linked partners too (true)."));
        Edit("edit.trim", "cutlass_trim_clip", "/v1/edit/trim", "Trim a clip's start or end, optionally rippling later clips.",
            S().Str("clipId", "The clip.", true).Str("edge", "start or end.", true, "start", "end").Int("deltaFrames", "Frames to move the edge.").Int("toFrame", "Move the edge to this frame.", false, 0)
                .Bool("ripple", "Shift later clips (false).").Bool("includeLinked", "Trim linked partners too (true)."));
        Edit("edit.roll", "cutlass_roll_edit", "/v1/edit/roll", "Move the cut between two butting clips.",
            S().Str("leftClipId", "The outgoing clip.", true).Str("rightClipId", "The incoming clip.", true).Int("deltaFrames", "Frames to move the cut.", true).Bool("includeLinked", "Roll linked partners too (true)."));
        Edit("edit.slip", "cutlass_slip_clip", "/v1/edit/slip", "Change which source part a clip shows without moving it.",
            S().Str("clipId", "The clip.", true).Int("deltaFrames", "Frames to slip the source.", true).Bool("includeLinked", "Slip linked partners too (true)."));
        Edit("edit.split", "cutlass_split", "/v1/edit/split", "Split clips at a frame (default: every unlocked track).",
            S().Int("frame", "Timeline frame.", true, 0).StrArray("trackIds", "Only these tracks.").StrArray("clipIds", "Only these clips.").Bool("includeLinked", "Split linked partners too (true)."));
        Edit("edit.delete", "cutlass_delete_clips", "/v1/edit/delete", "Delete clips, leaving gaps or closing them up (ripple).",
            S().StrArray("clipIds", "Clip ids.", true, 1000).Bool("ripple", "Close the gaps (false).").Bool("includeLinked", "Delete linked partners too (true)."));
        Edit("edit.remove-ranges", "cutlass_remove_ranges", "/v1/edit/remove-ranges", "Cut time ranges out of all unlocked tracks (or the given ones) and close the gaps.",
            S().ObjArray("ranges", "Ranges [start, end) in frames (at most 500).", range, true, 500).StrArray("trackIds", "Only these tracks.").Bool("ripple", "Close the gaps (true)."));
        Edit("edit.close-gap", "cutlass_close_gap", "/v1/edit/close-gap", "Close the gap at a frame on one track by moving later clips left.",
            S().Str("trackId", "The track.", true).Int("gapStart", "A frame inside or at the start of the gap.", true, 0));
        var transform = S().Num("x", "Offset from centre in pixels.").Num("y", "Offset from centre in pixels.").Num("scale", "Scale (1 fits).")
            .Num("cropLeft", "0 to 0.9.").Num("cropTop", "0 to 0.9.").Num("cropRight", "0 to 0.9.").Num("cropBottom", "0 to 0.9.");
        var color = S().Num("brightness", "-1 to 1 (0).").Num("contrast", "0 to 2 (1).").Num("saturation", "0 to 3 (1).").Num("gamma", "0.1 to 4 (1).");
        Edit("edit.clip-props", "cutlass_set_clip_props", "/v1/edit/clip-props", "Opacity, transform, colour, gain, fades, enable.",
            S().StrArray("clipIds", "Clip ids.", true, 1000).Bool("enabled", "Enable or disable.").Num("opacity", "0 to 1 (video and titles).")
                .Obj("transform", "Video placement (fields optional).", transform).Obj("color", "Colour adjustment (fields optional).", color)
                .Num("gainDb", "Clip gain in dB (audio).").Int("fadeInFrames", "Fade-in frames.", false, 0).Int("fadeOutFrames", "Fade-out frames.", false, 0));
        Edit("edit.speed", "cutlass_set_speed", "/v1/edit/speed", "Set clip speed in percent.",
            S().StrArray("clipIds", "Clip ids.", true, 1000).Int("percent", "25 to 400.", true, 25, 400).Bool("rippleFollowing", "Shift later clips (true)."));
        Edit("edit.link", "cutlass_link_clips", "/v1/edit/link", "Link clips so they select and move together.", S().StrArray("clipIds", "Clip ids.", true, 1000));
        Edit("edit.unlink", "cutlass_unlink_clips", "/v1/edit/unlink", "Unlink clips.", S().StrArray("clipIds", "Clip ids.", true, 1000));
        Edit("edit.volume-keys", "cutlass_set_volume_keys", "/v1/edit/volume-keys", "Replace an audio clip's volume envelope (empty clears).",
            S().Str("clipId", "The audio clip.", true).ObjArray("keys", "Keys relative to the clip start.", S().Int("frame", "Frame from the clip start.", true, 0).Num("db", "Level in dB.", true), true, 1000));
        var kinds = new[] { "crossfade", "dipToBlack", "dipToWhite", "wipeLeft", "wipeRight", "slideLeft", "slideRight", "circleOpen", "audioCrossfade" };
        Edit("edit.transition.add", "cutlass_add_transition", "/v1/edit/transition/add", "Add a transition on the cut between two butting clips.",
            S().Str("trackId", "The track.", true).Int("cutFrame", "The cut frame.", true, 0).Str("kind", "Transition kind (crossfade).", false, kinds).Int("durationFrames", "Length in frames (at least 2).", true, 2));
        Edit("edit.transition.update", "cutlass_update_transition", "/v1/edit/transition/update", "Change a transition's kind or length.",
            S().Str("transitionId", "The transition.", true).Str("kind", "New kind.", false, kinds).Int("durationFrames", "New length.", false, 2));
        Edit("edit.transition.remove", "cutlass_remove_transitions", "/v1/edit/transition/remove", "Remove transitions.", S().StrArray("transitionIds", "Transition ids.", true, 1000));
        var anchors = new[] { "center", "lowerThird", "top" };
        SchemaBuilder Title(bool requireText) => S().Str("text", "The text (lines separated by \\n).", requireText).Str("font", "builtin:Inter-Bold, a system family or a font file.")
            .Num("size", "Font size in canvas pixels.").Str("color", "#RRGGBB or #RRGGBBAA.").Num("outlineWidth", "Outline width (0: none).").Str("outlineColor", "#RRGGBB or #RRGGBBAA.")
            .Bool("box", "Background box.").Str("boxColor", "#RRGGBB or #RRGGBBAA.").Num("boxPadding", "Box padding in pixels.").Str("anchor", "Position preset.", false, anchors)
            .Num("offsetX", "Offset from the anchor in pixels.").Num("offsetY", "Offset from the anchor in pixels.");
        Edit("edit.title.add", "cutlass_add_title", "/v1/edit/title/add", "Add a text title on a video track.",
            S().Str("trackId", "A video track.", true).Int("at", "Timeline frame.", true, 0).Int("durationFrames", "Length in frames.", true, 1).Str("mode", "overwrite (default) or insert.", false, modes)
                .Obj("title", "Text and style (only text is required).", Title(true), true).Num("opacity", "0 to 1.").Int("fadeInFrames", "Fade-in frames.", false, 0).Int("fadeOutFrames", "Fade-out frames.", false, 0));
        Edit("edit.title.update", "cutlass_update_title", "/v1/edit/title/update", "Change a title's text, style, opacity or fades.",
            S().Str("clipId", "The title clip.", true).Obj("title", "Fields to change.", Title(false)).Num("opacity", "0 to 1.")
                .Int("fadeInFrames", "Fade-in frames.", false, 0).Int("fadeOutFrames", "Fade-out frames.", false, 0));
        Edit("edit.marker.add", "cutlass_add_marker", "/v1/edit/marker/add", "Add a marker.",
            S().Int("frame", "Timeline frame.", true, 0).Str("name", "Label.").Str("color", "#RRGGBB."));
        Edit("edit.marker.update", "cutlass_update_marker", "/v1/edit/marker/update", "Move or rename a marker.",
            S().Str("markerId", "The marker.", true).Int("frame", "New frame.", false, 0).Str("name", "New label.").Str("color", "#RRGGBB."));
        Edit("edit.marker.remove", "cutlass_remove_markers", "/v1/edit/marker/remove", "Remove markers.", S().StrArray("markerIds", "Marker ids.", true, 1000));
        Edit("edit.in-out", "cutlass_set_in_out", "/v1/edit/in-out", "Set or clear the In and Out points (null clears).",
            S().IntOrNull("inFrame", "In frame or null.").IntOrNull("outFrame", "Out frame (exclusive) or null."));
        Edit("edit.track.add", "cutlass_add_track", "/v1/edit/track/add", "Add a video or audio track.",
            S().Str("kind", "video or audio.", true, "video", "audio").Int("index", "Kind index (1 is V1 or A1); default: on top or bottom.", false, 1));
        Edit("edit.track.update", "cutlass_update_track", "/v1/edit/track/update", "Track name, hidden, locked, targeted, mute, solo and gain.",
            S().Str("trackId", "The track.", true).Str("name", "New name.").Bool("hidden", "Video: hide.").Bool("locked", "Lock against edits.").Bool("targeted", "Target for placing.")
                .Bool("muted", "Audio: mute.").Bool("solo", "Audio: solo.").Num("gainDb", "Audio: track gain in dB."));
        Edit("edit.track.remove", "cutlass_remove_track", "/v1/edit/track/remove", "Remove a track and its clips.", S().Str("trackId", "The track.", true));
        Edit("edit.sequence", "cutlass_set_sequence", "/v1/edit/sequence", "Sequence size and frame rate.",
            S().Int("width", "Canvas width (even).", false, 16, 7680).Int("height", "Canvas height (even).", false, 16, 4320).Str("frameRate", "Frame rate such as 30000/1001."));
        Edit("undo", "cutlass_undo", "/v1/undo", "Undo your own last step, never the user's.", S());
        Edit("redo", "cutlass_redo", "/v1/redo", "Redo your own last undone step.", S());
        Edit("batch.begin", "cutlass_begin_batch", "/v1/batch/begin", "Group edits into one undo step \"Agent: label\".", S().Str("label", "Short and human, for example Remove silences.", true));
        Edit("batch.end", "cutlass_end_batch", "/v1/batch/end", "Close the batch as one undo step.", S());
        Edit("batch.cancel", "cutlass_cancel_batch", "/v1/batch/cancel", "Revert every edit since begin_batch and close the batch.", S());

        // Export.
        Locked("exports.start", "cutlass_start_export", "POST", "/v1/exports", "Render to a file inside the write folders; follow it with export_status.",
            S().Str("preset", "Preset id such as h264 (see capabilities).", true).Str("quality", "draft, good, high or best (high).", false, "draft", "good", "high", "best").Int("height", "Output height.", false, 16)
                .Int("width", "GIF width.", false, 16).Str("range", "whole (default) or inOut; use start and end for a frame range.").Int("start", "First frame of a custom range.", false, 0)
                .Int("end", "End frame of a custom range.", false, 0).Str("outputPath", "The output file.", true).Bool("limitPeaks", "Peak limiter (true).").Bool("overwrite", "Replace an existing file (false)."));
        Read("exports.list", "cutlass_list_exports", "GET", "/v1/exports", "This process's exports, oldest first.", S());
        Read("exports.get", "cutlass_export_status", "GET", "/v1/exports/{exportId}", "An export's state and progress; waits up to waitMs for a change.",
            S().Str("exportId", "The export id.", true).Int("waitMs", "Wait at most this long for a change (25000 max).", false, 0, 25000));
        Locked("exports.cancel", "cutlass_cancel_export", "POST", "/v1/exports/{exportId}/cancel", "Cancel an export gracefully.", S().Str("exportId", "The export id.", true));

        return [.. list];
    }
}
