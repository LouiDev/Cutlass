using System.Collections.Frozen;

namespace Cutlass.App.Services;

/// <summary>
/// The ids of every command the keymap and the menus can name (DESIGN 7). A command that is not registered is simply
/// not executed, so its key falls through.
/// </summary>
public static class CommandIds
{
    /// <summary>Space: play or pause.</summary>
    public const string PlayPause = "transport.playPause";

    /// <summary>Shift+Space: play In to Out.</summary>
    public const string PlayInToOut = "transport.playInToOut";

    /// <summary>L: play forward, again for 2x and 4x.</summary>
    public const string ShuttleForward = "transport.shuttleForward";

    /// <summary>K: pause.</summary>
    public const string Pause = "transport.pause";

    /// <summary>J: reverse shuttle (until M8 it steps back one frame).</summary>
    public const string ShuttleReverse = "transport.shuttleReverse";

    /// <summary>Ctrl+Shift+L: loop.</summary>
    public const string ToggleLoop = "transport.loop";

    /// <summary>Left: one frame back.</summary>
    public const string StepBackward = "playhead.stepBackward";

    /// <summary>Right: one frame forward.</summary>
    public const string StepForward = "playhead.stepForward";

    /// <summary>Shift+Left: ten frames back.</summary>
    public const string StepBackward10 = "playhead.stepBackward10";

    /// <summary>Shift+Right: ten frames forward.</summary>
    public const string StepForward10 = "playhead.stepForward10";

    /// <summary>Up: previous edit point.</summary>
    public const string PreviousEdit = "playhead.previousEdit";

    /// <summary>Down: next edit point.</summary>
    public const string NextEdit = "playhead.nextEdit";

    /// <summary>Home: sequence start.</summary>
    public const string GoToStart = "playhead.start";

    /// <summary>End: sequence end.</summary>
    public const string GoToEnd = "playhead.end";

    /// <summary>Shift+I: go to In.</summary>
    public const string GoToIn = "playhead.goToIn";

    /// <summary>Shift+O: go to Out.</summary>
    public const string GoToOut = "playhead.goToOut";

    /// <summary>I: mark In.</summary>
    public const string MarkIn = "mark.in";

    /// <summary>O: mark Out.</summary>
    public const string MarkOut = "mark.out";

    /// <summary>X: mark the clip under the playhead.</summary>
    public const string MarkClip = "mark.clip";

    /// <summary>Ctrl+Shift+X: clear In and Out.</summary>
    public const string ClearInOut = "mark.clear";

    /// <summary>M: add a marker.</summary>
    public const string AddMarker = "marker.add";

    /// <summary>Shift+M: next marker.</summary>
    public const string NextMarker = "marker.next";

    /// <summary>Ctrl+Shift+M: previous marker.</summary>
    public const string PreviousMarker = "marker.previous";

    /// <summary>V: Select tool.</summary>
    public const string ToolSelect = "tool.select";

    /// <summary>B: Ripple tool.</summary>
    public const string ToolRipple = "tool.ripple";

    /// <summary>N: Roll tool.</summary>
    public const string ToolRoll = "tool.roll";

    /// <summary>Y: Slip tool.</summary>
    public const string ToolSlip = "tool.slip";

    /// <summary>C: Razor tool.</summary>
    public const string ToolRazor = "tool.razor";

    /// <summary>S: toggle snapping.</summary>
    public const string ToggleSnap = "timeline.toggleSnap";

    /// <summary>Ctrl+K: split at the playhead.</summary>
    public const string Split = "edit.split";

    /// <summary>Ctrl+Shift+K: split all unlocked tracks.</summary>
    public const string SplitAll = "edit.splitAll";

    /// <summary>Delete, Backspace: lift.</summary>
    public const string Lift = "edit.lift";

    /// <summary>Shift+Delete: ripple delete.</summary>
    public const string RippleDelete = "edit.rippleDelete";

    /// <summary>Q: ripple trim start to the playhead.</summary>
    public const string RippleTrimStart = "edit.rippleTrimStart";

    /// <summary>W: ripple trim end to the playhead.</summary>
    public const string RippleTrimEnd = "edit.rippleTrimEnd";

    /// <summary>Alt+Left: nudge the selection one frame left.</summary>
    public const string NudgeLeft = "edit.nudgeLeft";

    /// <summary>Alt+Right: nudge the selection one frame right.</summary>
    public const string NudgeRight = "edit.nudgeRight";

    /// <summary>Comma: insert from Source (with nothing in Source: the whole media selected in the bin).</summary>
    public const string Insert = "edit.insert";

    /// <summary>Period: overwrite from Source (with nothing in Source: the whole media selected in the bin).</summary>
    public const string Overwrite = "edit.overwrite";

    /// <summary>F: match frame.</summary>
    public const string MatchFrame = "edit.matchFrame";

    /// <summary>Ctrl+D: default video transition.</summary>
    public const string AddTransition = "edit.addTransition";

    /// <summary>Ctrl+Shift+D: default audio crossfade.</summary>
    public const string AddAudioCrossfade = "edit.addAudioCrossfade";

    /// <summary>Ctrl+T: new title at the playhead.</summary>
    public const string NewTitle = "edit.newTitle";

    /// <summary>Ctrl+R: speed dialog.</summary>
    public const string Speed = "edit.speed";

    /// <summary>Ctrl+L: link or unlink.</summary>
    public const string LinkUnlink = "edit.link";

    /// <summary>Shift+E: enable or disable clips.</summary>
    public const string ToggleEnabled = "edit.toggleEnabled";

    /// <summary>Ctrl+C: copy.</summary>
    public const string Copy = "edit.copy";

    /// <summary>Ctrl+X: cut.</summary>
    public const string Cut = "edit.cut";

    /// <summary>Ctrl+V: paste (overwrite).</summary>
    public const string Paste = "edit.paste";

    /// <summary>Ctrl+Shift+V: paste (insert).</summary>
    public const string PasteInsert = "edit.pasteInsert";

    /// <summary>Ctrl+A: select all.</summary>
    public const string SelectAll = "edit.selectAll";

    /// <summary>Esc: cancel a drag, else deselect.</summary>
    public const string Escape = "edit.escape";

    /// <summary>Ctrl+Z: undo.</summary>
    public const string Undo = "edit.undo";

    /// <summary>Ctrl+Shift+Z, Ctrl+Y: redo.</summary>
    public const string Redo = "edit.redo";

    /// <summary>Plus: zoom in.</summary>
    public const string ZoomIn = "view.zoomIn";

    /// <summary>Minus: zoom out.</summary>
    public const string ZoomOut = "view.zoomOut";

    /// <summary>Shift+Z: zoom to fit.</summary>
    public const string ZoomToFit = "view.zoomToFit";

    /// <summary>G: safe guides.</summary>
    public const string ToggleGuides = "view.guides";

    /// <summary>P: toggle proxies.</summary>
    public const string ToggleProxies = "view.proxies";

    /// <summary>Ctrl+Comma: settings.</summary>
    public const string Settings = "app.settings";

    /// <summary>Ctrl+N: new project.</summary>
    public const string NewProject = "file.new";

    /// <summary>Ctrl+O: open a project.</summary>
    public const string OpenProject = "file.open";

    /// <summary>Ctrl+S: save.</summary>
    public const string Save = "file.save";

    /// <summary>Ctrl+Shift+S: save as.</summary>
    public const string SaveAs = "file.saveAs";

    /// <summary>Ctrl+I: import media.</summary>
    public const string Import = "file.import";

    /// <summary>Ctrl+E: export.</summary>
    public const string Export = "file.export";

    /// <summary>Ctrl+Q: quit.</summary>
    public const string Quit = "app.quit";

    /// <summary>Menu: move the selected clip into sync with its linked partner.</summary>
    public const string MoveIntoSync = "clip.moveIntoSync";

    /// <summary>Menu and + Track: add a video track.</summary>
    public const string AddVideoTrack = "sequence.addVideoTrack";

    /// <summary>Menu and + Track: add an audio track.</summary>
    public const string AddAudioTrack = "sequence.addAudioTrack";

    /// <summary>Menu: remove the selected track.</summary>
    public const string RemoveSelectedTrack = "sequence.removeTrack";

    /// <summary>Menu: select the clips inside the In/Out range.</summary>
    public const string SelectInOutRange = "sequence.selectInOut";

    /// <summary>Menu: ripple delete the In/Out range on the unlocked tracks.</summary>
    public const string RippleDeleteInOutRange = "sequence.rippleDeleteInOut";

    /// <summary>Menu: close the selected gap.</summary>
    public const string CloseGap = "sequence.closeGap";

    /// <summary>Menu: the sequence settings dialog.</summary>
    public const string SequenceSettings = "sequence.settings";

    /// <summary>Menu: show the Timeline monitor.</summary>
    public const string ShowTimelineMonitor = "view.timelineMonitor";

    /// <summary>Menu: show the Source monitor.</summary>
    public const string ShowSourceMonitor = "view.sourceMonitor";

    /// <summary>File menu: Relink media... (DESIGN 11), the Relink dialog with every offline media file.</summary>
    public const string RelinkMedia = "file.relinkMedia";

    /// <summary>Help menu: About Cutlass (the Settings dialog's About page, DESIGN 10).</summary>
    public const string About = "help.about";

    /// <summary>Help menu: open the log folder in the file manager.</summary>
    public const string OpenLogFolder = "help.openLogFolder";

    /// <summary>Agent banner and View menu: commit the agent's open batch and revoke its lock (AGENT_INTEGRATION 3.5).</summary>
    public const string AgentTakeOver = "agent.takeOver";

    /// <summary>Agent banner and View menu: pause or resume the agent's mutating calls (AGENT_INTEGRATION 3.4).</summary>
    public const string AgentPauseResume = "agent.pauseResume";

    /// <summary>Agent banner: keep the viewer on what the agent works on (AGENT_INTEGRATION 6.2).</summary>
    public const string AgentToggleFollow = "agent.follow";

    /// <summary>Agent menu and the status bar badge: show the AGENT tab of the left panel (AGENT_INTEGRATION 6.3).</summary>
    public const string ShowAgentPanel = "view.agentPanel";

    /// <summary>F9, Agent menu and the Agent panel: start or stop the MCP (agent) server for this session (AGENT_INTEGRATION 6.6).</summary>
    public const string AgentToggleServer = "agent.toggleServer";

    /// <summary>
    /// Agent menu and the Agent panel: copy the briefing for the client registered last, or the generic briefing when
    /// none was (AGENT_INTEGRATION 6.6 and 9.4).
    /// </summary>
    public const string AgentCopyBriefing = "agent.copyBriefing";

    /// <summary>Agent menu, "Copy briefing for" > HTTP only: the briefing with curl examples (AGENT_INTEGRATION 9.4).</summary>
    public const string AgentCopyBriefingHttp = "agent.copyBriefingHttp";

    /// <summary>
    /// The prefix of the "Copy briefing for" submenu's commands, one per client profile (<see cref="AgentCopyBriefingFor"/>);
    /// they are registered once the profiles are loaded.
    /// </summary>
    public const string AgentCopyBriefingPrefix = "agent.copyBriefing.";

    /// <summary>Agent menu and the Agent panel: the client list, "Connect an agent client..." (AGENT_INTEGRATION 9.4).</summary>
    public const string AgentConnectClients = "agent.connectClients";

    /// <summary>The command id that copies the briefing for one client profile.</summary>
    /// <param name="profileId">The profile id.</param>
    /// <returns>The command id.</returns>
    public static string AgentCopyBriefingFor(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return AgentCopyBriefingPrefix + profileId;
    }

    /// <summary>
    /// True when a command may run while an agent holds the project: <see cref="AllowedWhileAgentHolds"/> plus the
    /// per-profile briefing commands, which touch no project data either.
    /// </summary>
    /// <param name="id">The command id.</param>
    /// <returns>True when allowed.</returns>
    public static bool IsAllowedWhileAgentHolds(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return AllowedWhileAgentHolds.Contains(id) || id.StartsWith(AgentCopyBriefingPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// The commands that still run while an agent holds or pauses the project (AGENT_INTEGRATION 3.7 and 3.8): they
    /// change only the view, the playhead, playback, the selection, files on disk (save, export) or settings, never
    /// the project. Transport and loop, playhead steps, edit points, start and end, go to In/Out, marker navigation,
    /// zoom, guides, proxies, snapping, tool selection, monitor switches, match frame, select all, copy, Escape, Save,
    /// Save As, Export, Settings and Quit as the spec lists them. Gap filled here: About (a Settings page), Open Log
    /// Folder and the agent commands themselves (Take over, Pause/Resume, Follow, the Agent panel) are allowed too,
    /// because they cannot change the project. The Agent menu (AGENT_INTEGRATION 6.6) is allowed as well: Stop asks
    /// before it takes the project back, and the briefing and client commands touch no project data (the per-client
    /// briefing commands are allowed through <see cref="IsAllowedWhileAgentHolds"/>). The routed
    /// mark commands of <see cref="RoutedToSource"/> are allowed only while they target the Source monitor.
    /// </summary>
    public static IReadOnlySet<string> AllowedWhileAgentHolds { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        PlayPause, PlayInToOut, ShuttleForward, Pause, ShuttleReverse, ToggleLoop,
        StepBackward, StepForward, StepBackward10, StepForward10, PreviousEdit, NextEdit, GoToStart, GoToEnd,
        GoToIn, GoToOut, NextMarker, PreviousMarker,
        ZoomIn, ZoomOut, ZoomToFit, ToggleGuides, ToggleProxies, ToggleSnap,
        ToolSelect, ToolRipple, ToolRoll, ToolSlip, ToolRazor,
        ShowTimelineMonitor, ShowSourceMonitor, MatchFrame,
        SelectAll, Copy, Escape,
        Save, SaveAs, Export, Settings, Quit,
        About, OpenLogFolder,
        AgentTakeOver, AgentPauseResume, AgentToggleFollow, ShowAgentPanel,
        AgentToggleServer, AgentCopyBriefing, AgentCopyBriefingHttp, AgentConnectClients,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The routed commands (DESIGN 4.3) that edit the timeline In/Out but only view state in the Source monitor
    /// (Source In/Out): allowed while an agent holds the project only when the Source monitor is their target.
    /// </summary>
    public static IReadOnlySet<string> RoutedToSource { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        MarkIn, MarkOut, MarkClip, ClearInOut,
    }.ToFrozenSet(StringComparer.Ordinal);
}
