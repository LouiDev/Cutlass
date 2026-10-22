using System.Globalization;
using System.Text;
using Cutlass.Core.Time;

namespace Cutlass.App.Services;

/// <summary>
/// The developer smoke script run by <c>--smoke-script &lt;file&gt;</c> (the repository guide, Commands). One command per line;
/// blank lines and lines starting with <c>#</c> are skipped; arguments are separated by spaces and may be quoted with
/// double quotes. Relative paths resolve against the script's folder. The commands are listed in
/// <see cref="Commands"/>; <see cref="SmokeRunner"/> executes them through the same view model commands the UI uses
/// and stops at the first failure.
/// </summary>
public static class SmokeScript
{
    /// <summary>Every command with its usage, as printed for an unknown command.</summary>
    public static IReadOnlyDictionary<string, string> Commands { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["new"] = "new: start a new untitled project",
        ["open"] = "open <file.cutlass>: open a project (and its .cutlass.user sidecar)",
        ["save"] = "save <file.cutlass>: save the project and its sidecar",
        ["import"] = "import <path>...: import media into the bin (starts an untitled project when none is open)",
        ["insert"] = "insert <media>: ',' insert of the whole media at the playhead on the targeted tracks",
        ["overwrite"] = "overwrite <media>: '.' overwrite at the playhead",
        ["place"] = "place <media> <track> <pos> [insert]: a drop from the bin onto a track",
        ["drop-files"] = "drop-files <track> <pos> <path>... [insert]: a file manager drop onto a track (imports, then places)",
        ["playhead"] = "playhead <pos>: move the playhead (frame, start, middle, end, HH:MM:SS:FF, +N, -N)",
        ["frame"] = "frame [timeout=s]: request the playhead frame and wait until the viewer presents it",
        ["split"] = "split: Ctrl+K at the playhead",
        ["select-at"] = "select-at <pos> [<track>...]: select the clips under a frame (linked selection applies)",
        ["select-all"] = "select-all: Ctrl+A",
        ["deselect"] = "deselect: Esc",
        ["delete"] = "delete: lift the selection",
        ["ripple-delete"] = "ripple-delete: Shift+Delete",
        ["undo"] = "undo: Ctrl+Z",
        ["redo"] = "redo: Ctrl+Shift+Z",
        ["zoom-fit"] = "zoom-fit: Shift+Z",
        ["expect-length"] = "expect-length <frames>: fail unless the sequence has this many frames",
        ["expect-clips"] = "expect-clips [<track>] <count>: fail unless the track (or the whole sequence) holds this many clips",
        ["export"] = "export <preset> <path> [quality=draft|good|high|best] [range=whole|inout] [height=<custom height>] [width=<GIF width>] [timeout=s]: export through the export dialog and wait",
        ["probe"] = "probe <path> [frames=<n>|frames=export] [vcodec=x] [acodec=x] [size=WxH] [bt709] [container=<name>] [novideo] [noaudio] [pixfmt=x] [tag=<fourcc>] [duration=<s>|export [tolerance=<s>]]: check a file with ffprobe",
        ["screenshot"] = "screenshot <file.png>: render the main window to a PNG",
        ["play"] = "play [speed=1|2|4]: start playback at the playhead (Space, then L for 2x and 4x), wait for the first frame and log the clock against the presented frames every 0.5 s",
        ["stop"] = "stop [max-drift=<frames>]: stop playback (K), report the clock drift against the wall clock, the presented and dropped frames and the parked frame",
        ["wait-jobs"] = "wait-jobs [timeout=s]: wait until the media jobs (index, thumbnails, waveform, proxy) are idle and log which artifacts exist",
        ["mute"] = "mute <track>: toggle a track's mute (applies live while playing)",
        ["solo"] = "solo <track>: toggle a track's solo (applies live while playing)",
        ["expect-meter"] = "expect-meter [min=<dBFS>] [max=<dBFS>]: fail unless the master meter (the louder channel) is within the range",
        ["expect-artifacts"] = "expect-artifacts <media> [thumbs] [peaks] [proxy]: fail unless the media's thumbnail sheets, waveform peaks or proxy exist",
        ["jobs"] = "jobs open|close: open or close the jobs popover (it is a separate window, so window screenshots do not show it)",
        ["wait"] = "wait <ms>: let the UI settle",
        ["log"] = "log <text>: write a line to the log",
        ["source-open"] = "source-open <media> [<pos>]: open media in the Source monitor (bin double-click) and optionally park it",
        ["source-in"] = "source-in <pos>: move the Source playhead (frame, timecode, +N, -N, end) and press I",
        ["source-out"] = "source-out <pos>: move the Source playhead and press O (Out includes that frame)",
        ["source-insert"] = "source-insert: ',' with media in Source (the Source range at the timeline In or playhead)",
        ["source-overwrite"] = "source-overwrite: '.' with media in Source",
        ["monitor"] = "monitor timeline|source: switch the viewer",
        ["match-frame"] = "match-frame [frame=<n>]: F; fail unless Source opened the clip under the playhead (at Source frame n)",
        ["key"] = "key <chord> [edit]: press a chord through the keymap (for example OemComma, Q, Ctrl+Shift+D); edit fails when the project did not change",
        ["timecode"] = "timecode <text>: click the viewer timecode, type the text (timecode or +N/-N) and press Enter",
        ["copy"] = "copy: Ctrl+C on the selection",
        ["cut"] = "cut: Ctrl+X on the selection",
        ["paste"] = "paste [insert]: Ctrl+V at the playhead (Ctrl+Shift+V with insert)",
        ["trim-start"] = "trim-start: Q, ripple trim the start of the clips under the playhead to it",
        ["trim-end"] = "trim-end: W, ripple trim the end of the clips under the playhead to it",
        ["add-transition"] = "add-transition [kind|audio] [frames]: a transition at the cut nearest the playhead on the targeted tracks (default crossfade, 1 s)",
        ["speed"] = "speed <percent> [noripple]: set the speed of the selected clips (the speed dialog's edit)",
        ["select-gap"] = "select-gap <track> <pos>: select the gap of a track that contains the frame",
        ["close-gap"] = "close-gap: close the selected gap",
        ["add-track"] = "add-track video|audio: add a track (the + Track menu)",
        ["expect-duration"] = "expect-duration <frames>: fail unless the sequence has this many frames",
        ["expect-playhead"] = "expect-playhead <pos>: fail unless the timeline playhead is there",
        ["expect-source"] = "expect-source [media=<media>] [playhead=<n>] [in=<n>|-] [out=<n>|-]: check the Source monitor",
        ["expect-transitions"] = "expect-transitions <track> <count>: fail unless the track holds this many transitions",
        ["source-drop"] = "source-drop video|audio|both <track> <pos> [insert]: drag the Source range (a stream handle or the canvas) onto a track",
        ["effect-drop"] = "effect-drop <effect id> <track> <pos>: drop an Effects tab item (transition:Kind, title:Name, adjust:Name) on a track",
        ["select-transition"] = "select-transition <track> [index]: click a transition block",
        ["volume-key"] = "volume-key <track> <pos> <dB>: select the audio clip under the frame and add a volume keyframe there",
        ["expect-section"] = "expect-section <key prefix> [header=<text>]: fail unless the Inspector shows that section (clip, title, transition, gap, sequence)",
        ["settings"] = "settings [key] [page=general|media|playback|agents|about] [autosave=<min>] [transition=<s>] [still=<s>] [title=<s>] [cache-gb=<n>] [proxy=auto|always|never] [quality=auto|full|half|quarter] [proxies=true|false] [confirm-delete=true|false] [export-preset=<id>] [ffmpeg=<path>|path-search] [agent-read=<folder>] [agent-write=<folder>] [agent-home=<folder>] [expect-problem=<text>] [validate] [clear-cache] [apply|cancel]: drive the Settings dialog (key opens it with Ctrl+,; agent-home replaces the agent client sandbox with an empty fake home inside the script folder)",
        ["show-dialog"] = "show-dialog settings|relink|export|speed|sequence|mcp-start|connect-clients [page=general|media|playback|agents|about] [media=<media>] [height=<px>] screenshot=<file.png>: open the dialog through its command, render its real window to a PNG and close it (relink without media= is File > Relink media...; mcp-start is the first-start dialog of Agent > Start MCP server; connect-clients is Agent > Connect an agent client...; height= renders a taller window so a long page shows its lower groups)",
        ["remove-file"] = "remove-file <path>: delete a file (retried while a process still holds it), for example to take media offline",
        ["expect-offline"] = "expect-offline <media> [online]: fail unless the media is offline (or, with online, not offline) in the bin",
        ["expect-setting"] = "expect-setting <property> <value>: fail unless the AppSettings property has the value in effect and in settings.json",
        ["agent-connect"] = "agent-connect [name=<agent>]: read endpoint.json and the token, connect an AgentClient over real HTTP and check status",
        ["agent-call"] = "agent-call <tool> <json> [expect=<code>] [save=<var>=<path>]... [png=<file.png>]: POST /v1/call; fails unless the code is expect (default ok); $var in later JSON is replaced; png= writes the frame image",
        ["expect-locked"] = "expect-locked [holder=<name>] [paused] [pending]: fail unless the banner and /v1/lock show an agent holding (paused, or waiting) the project",
        ["expect-unlocked"] = "expect-unlocked: fail unless the lock is free in the banner and in /v1/lock",
        ["agent-takeover"] = "agent-takeover: the banner's Take over",
        ["agent-pause"] = "agent-pause: the banner's Pause",
        ["agent-resume"] = "agent-resume: the banner's Resume",
        ["expect-readonly"] = "expect-readonly [keys=<chord>,<chord>...]: fail unless gated commands report CanExecute false and the keys (default Ctrl+Z,Delete) are claimed without changing the project or the API revision",
        ["expect-agent-log"] = "expect-agent-log <tool> [code=<code>]: fail unless the Agent panel's call log holds the tool (with that code)",
        ["expect-duration-below"] = "expect-duration-below <frames>: fail unless the sequence has fewer frames (after an agent removed time)",
        ["expect-no-refused"] = "expect-no-refused: fail when the session write gate refused a user write during the run",
        ["panel"] = "panel media|effects|agent: show the media panel's MEDIA, EFFECTS or AGENT tab",
        ["agent-briefing"] = "agent-briefing [http|target=<profile id>] [save=<file>]: Agent > Copy briefing for your agent (the client registered last, else the generic text), or a Copy briefing for item (http is its HTTP only item); fails unless every placeholder is filled and the text names the project and the target's tail; save= writes it with the token masked",
        ["write-file"] = "write-file <path> <text> [json]: write a file (\\n is a line end, $var a saved value such as $clienthome; json turns single quotes into double ones), for example a client configuration in the sandbox home",
        ["agent-clients"] = "agent-clients [expect=<id>:<state>,...] [current=<n>]: check every agent client of the sandbox registry again, log the rows and compare states (not-installed, not-registered, current, stale, foreign, error)",
        ["agent-client"] = "agent-client <id> register|update|remove [expect=<state>]: run a client row's button in the sandbox registry (the smoke dialog service confirms) and check the state",
        ["menu-start-mcp"] = "menu-start-mcp: Agent > Start MCP server through the menu command (the first-start dialog answers Start without registering); waits for the server and checks the badge and the menu text",
        ["menu-stop-mcp"] = "menu-stop-mcp: Agent > Stop MCP server through the menu command; waits until the server is off and checks the badge and the menu text",
        ["show-menu"] = "show-menu <header> [submenu=\"<header>[ > <header>]\"] screenshot=<file.png>: open a top-level menu (for example Agent) and the submenus given in turn (for example \"Copy briefing for\"), render the window with the open menus to a PNG and close them",
    };

    /// <summary>Parses a script.</summary>
    /// <param name="text">The script text.</param>
    /// <returns>The steps in order.</returns>
    /// <exception cref="FormatException">On an unknown command or an unterminated quote.</exception>
    public static IReadOnlyList<SmokeStep> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var steps = new List<SmokeStep>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var tokens = Tokenize(line, i + 1);
            var command = tokens[0].ToLowerInvariant();
            if (!Commands.ContainsKey(command))
            {
                throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Line {i + 1}: unknown command '{tokens[0]}'."));
            }

            steps.Add(new SmokeStep(i + 1, command, tokens[1..]));
        }

        return steps;
    }

    /// <summary>
    /// Resolves a timeline position: a frame number, <c>start</c>, <c>middle</c> (half the sequence length,
    /// floored), <c>end</c> (the sequence length), a timecode, or <c>+N</c>/<c>-N</c> relative to the playhead.
    /// </summary>
    /// <param name="text">The position.</param>
    /// <param name="sequenceEnd">The sequence length in frames.</param>
    /// <param name="playhead">The current playhead.</param>
    /// <param name="rate">The sequence rate (timecodes).</param>
    /// <param name="frame">The frame.</param>
    /// <returns>True when the text is a position.</returns>
    public static bool TryResolvePosition(string text, long sequenceEnd, long playhead, FrameRate rate, out long frame)
    {
        ArgumentNullException.ThrowIfNull(text);
        frame = 0;
        var t = text.Trim();
        switch (t.ToLowerInvariant())
        {
            case "start":
                frame = 0;
                return true;
            case "middle":
                frame = sequenceEnd / 2;
                return true;
            case "end":
                frame = sequenceEnd;
                return true;
        }

        if (t.Length > 1 && t[0] is '+' or '-' &&
            long.TryParse(t.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
        {
            frame = Math.Max(0, t[0] == '+' ? playhead + offset : playhead - offset);
            return true;
        }

        if (long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            frame = number;
            return true;
        }

        return Timecode.TryParse(t, rate, out frame);
    }

    private static string[] Tokenize(string line, int lineNumber)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var hasToken = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                hasToken = true;
            }
            else if (!quoted && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (quoted)
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Line {lineNumber}: unterminated quote."));
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return [.. tokens];
    }
}
