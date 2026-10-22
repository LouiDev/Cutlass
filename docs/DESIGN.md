# Cutlass Design: Product and UX

Status: **normative for v1**. Date: 2026-10-21.
Companion document: `docs/TECH_BRIEF.md` (architecture, ffmpeg engine, build order). On a UX question this document wins. On a technical question TECH_BRIEF wins. "Must" is a requirement. Numbers (pixels, milliseconds, colors) are the values to use unless a measurement proves them wrong.

---

## 1. Product summary

Cutlass is a desktop non-linear video editor for Windows and Linux (macOS is a nice-to-have). It is built with .NET 10 and Avalonia 11.3.22, and it does all media work by running the user's `ffmpeg` and `ffprobe`. It is for people who know the basics of Premiere or Resolve and want a small, fast, free tool for vlogs, tutorials, short films and social clips.

Principles:

1. **What you see is what you export.** Scrub frames, playback and export all come from one timeline compiler.
2. **Frame exact.** Timeline positions are integer frames, and exports contain exactly the planned frame and sample counts.
3. **Instant feedback.** Every action answers within one frame. A placeholder is shown until the exact frame arrives.
4. **Pro muscle memory.** Shortcuts follow Premiere conventions where one exists.
5. **Never lose work.** Saves are atomic, autosave runs in the background, and crash recovery is offered.
6. **Honest about the ffmpeg build.** If the build lacks an encoder or filter, the feature that needs it is greyed out with the reason.

---

## 2. Scope

### 2.1 v1 ships with
- **Projects.**
  - One sequence per project, saved as `.cutlass` JSON.
  - Resolution presets: 3840x2160, 2560x1440, 1920x1080, 1280x720, 1080x1920, 1080x1080, or a custom even size.
  - Frame rates: 23.976, 24, 25, 29.97, 30, 50, 59.94 or 60. Audio is stereo, 48 kHz.
  - The first clip placed into a never-used sequence sets the sequence size (rounded to even) and frame rate (nearest supported). A toast reports it, and the change can be undone.
  - Autosave, crash recovery, recent projects, relink.
- **Media.**
  - Video, audio and stills (PNG, JPG, WebP, BMP). Anything ffprobe opens is accepted.
  - Import by file dialog, by dropping onto the Media panel, or by dropping straight onto the timeline.
  - Background probe, frame index, poster, thumbnails, waveform, and a proxy where the policy calls for one.
- **Timeline.**
  - Up to 8 video and 8 audio tracks. A new project starts with V1, V2, A1 and A2.
  - Media, still and title clips.
  - Tools: Select, Ripple, Roll, Slip, Razor.
  - Overwrite and insert, trim, split, lift, ripple delete, gap close, snapping, markers, In/Out, linked A/V, copy and paste.
  - Track lock, hide, mute, solo, target and gain. Undo up to 500 steps.
- **Effects.**
  - `xfade` transitions: Crossfade, Dip to Black, Dip to White, Wipe Left, Wipe Right, Slide Left, Slide Right, Circle Open.
  - Audio crossfade, fade handles, text titles.
  - Constant speed 25% to 400% with pitch kept.
  - Clip gain and volume keyframes.
  - Picture in picture: position, scale, crop, opacity.
  - Color: brightness, contrast, saturation, gamma.
- **Preview.**
  - Frame-accurate scrub and step.
  - Real-time playback with audio, JKL forward at 1x, 2x and 4x.
  - Loop In to Out.
  - Quality Auto, Full, Half or Quarter, with automatic proxies.
  - Master meter and a dropped-frame indicator.
- **Export.**
  - Presets: H.264, H.265, VP9, AV1, ProRes 422 HQ, GIF, MP3, AAC, WAV, FLAC.
  - Whole sequence or In to Out, with quality and resolution options.
  - A queue with progress, ETA and cancel, and "Show ffmpeg command".

### 2.2 Late-milestone items (ship if time allows, else v1.1)
- Reverse shuttle on J (silent). Until it lands, J steps back one frame.
- Per-track audio meters. The master meter is in v1.
- Hardware encoder presets (NVENC, AMF, QSV), shown only after a passing test encode.
- Segmented export for timelines needing more than 64 ffmpeg inputs. Until then, such exports run after a warning.

### 2.3 Not in v1
- **Sequences:** nested or multiple sequences, multicam.
- **Keyframes:** only audio volume has them. Transform, opacity and color are static per clip.
- **Color:** LUTs, curves, wheels, scopes, HDR tone mapping (HDR gets a warning badge), 10-bit preview.
- **Time:** reverse clips, freeze frames, speed ramps.
- **Audio:** effects beyond gain, fades, crossfades and the export limiter, surround output, audio scrubbing, voice-over recording.
- **Visual effects:** subtitles, chroma key, masks, blur, stabilisation, tracking.
- **Extensibility:** plugins, scripting, a user keymap file.
- **Formats:** drop-frame timecode, deinterlacing, image sequences.
- **Organisation:** bin folders (the bin is one flat list with search), clip markers.
- **Distribution:** official macOS builds, bundled ffmpeg, collaboration.

---

## 3. Main window

The window opens maximized on first run. The default restored size is 1600x900 and the minimum is 1100x650.

```
+------------------------------------------------------------------------------------+
| File  Edit  Clip  Sequence  View  Help          beach-day *           [ Export ]   |
+------------------------+--------------------------------------+--------------------+
| MEDIA | EFFECTS        |  [Timeline|Source]   Auto v  [P] [G] |  INSPECTOR         |
| [search........] [=][#]|                                      |  > Clip            |
| +------+ +------+      |                                      |  > Transform       |
| |poster| |poster|      |        16:9 canvas, letterboxed       |  > Color           |
| +------+ +------+      |                                      |  > Audio           |
| beach.mp4  intro.mov   |                                      |                    |
| 01:04 [P]  00:12 (43%) |  00:00:12:04 / 00:03:41:17   1x  0dr |                    |
|                        |  |<   <|   >   |>   >|   loop          |                    |
+------------------------+--------------------------------------+--------------------+
| [V][B][N][Y][C] | magnet | link | 00:00:12:04 |  zoom ---o----  | +Track            |
+---------------+------------------------------------------------------------+-------+
| ruler         | 00:00      |00:05       |00:10       |00:15  markers, In/Out |       |
| V2 [V2][o][L] |        [ Title: Day One ]                                  |       |
| V1 [V1][o][L] | [beach.mp4 |||||||||][X][intro.mov ||||||||||||]           |       |
| ==============+============================================================+  M    |
| A1 [A1][M][S] | [~~~~ beach.mp4 ~~~~~][~~~~ intro.mov ~~~~~~~~~]           |  |    |
| A2 [A2][M][S] |          [~~~~~~~~~~ music.mp3 ~~~~~~~~~~~~~~~~~~~~~~~~~~~ |  |    |
+---------------+------------------------------------------------------------+-------+
| ffmpeg N-126308 ok | Jobs: proxy 2/5 (43%) | Cache 3.1/20 GB | 1920x1080 29.97 48k  |
+------------------------------------------------------------------------------------+
```

- **Proportions.** The top row takes 58% of the height and the timeline 42%. The top columns are 22% (Media), 56% (Viewer) and 22% (Inspector). All boundaries are `GridSplitter`s, and their positions are saved in settings.
- **Menu row.** It shows the project name, with `*` when there are unsaved changes. Export is the primary (Accent) button.
- **Agent banner** (AGENT_INTEGRATION 6.2). While an agent holds, pauses or waits for the project, a 32 px bar spans the window under the menu row:
  - Bg2 background, a 3 px Accent left edge (the Snap/Marker yellow while paused), a 1 px Border bottom line;
  - left: the agent's name in bold and its reason ("**Claude Code** is editing: Remove silences"), then in TextSecondary the last call and its age ("edit.split, 2 s ago"), the elapsed time (`tnum`) and the open batch;
  - right: a Follow toggle (on), Pause/Resume (secondary) and Take over (primary Accent); for "Ask me before an agent takes the project", Deny and Allow;
  - a waiting agent shows "Claude Code is waiting for the project"; after the lock ends the bar stays 3 s with "Claude Code finished", "You took over" or "Claude Code timed out".
  - While the lock is held or paused the editing UI is read-only (AGENT_INTEGRATION 3.7): only view, playback, selection, copy, save, export, settings and the agent commands run, the timeline lanes get a 1 px Accent frame, and the inspector, bin, Effects tab and Source insert are disabled. A key that would edit shows the toast "An agent is editing. Take over to edit." (at most every 3 s). Playback stays allowed and the agent never stops it.
  - Closing the window while an agent holds the project asks "Claude Code is editing. Take over and close?" before the save prompt.
- **Track stacking.** Video tracks stack upward from the center divider, with V1 nearest it. Audio tracks stack downward. Higher video tracks draw on top.

---

## 4. Panels

### 4.1 Welcome overlay
Shown when no project is open. It offers:
- New (Ctrl+N) and Open (Ctrl+O).
- Up to 10 recent projects. Missing files are greyed out with "Remove from list".
- A large "Drop media here" target. Dropping media creates an untitled project, imports the files and places them end to end on V1/A1. Its "Import media..." button does the same through the file dialog. On Linux, where OS file drops do not arrive (section 4.2), the target reads "Import media".
- The ffmpeg status. If ffmpeg is not found, the overlay shows the install hint (section 10) and "Locate ffmpeg...".

It covers the panels and the timeline only: the menu row with the agent banner and the status bar stay visible. While an agent holds the project its actions are disabled (AGENT_INTEGRATION 6.2).

### 4.2 Media panel
- **Views.**
  - Grid (default): 160x90 poster cards with name, duration and badges.
  - List: 24 px rows with Name, Duration, Resolution, FPS, Codec and Proxy.
  - Search filters by name as you type. Sort by name, duration or date added.
- **Hover skim.** Moving the mouse across a card scrubs its thumbnail sprites. Leaving restores the poster.
- **Badges:**
  - spinner while probing or indexing
  - proxy progress ring, then a `P` chip when ready
  - `VFR` (grey)
  - `HDR` (amber), with the tooltip "Shown and exported without tone mapping"
  - red diagonal stripes when offline
  - amber `CHANGED` when the file changed since import (section 11)
  - red `!` when unsupported, with the ffprobe error in the tooltip
- **Mouse actions.** Double-click opens the item in Source mode. Drag a card onto the timeline to place it.
- **Context menu:** Open in Source, Insert at playhead and Overwrite at playhead (the whole media, at the timeline In or playhead), Reveal in file manager, Create/Delete proxy, Relink..., Copy path, Remove from project. Removing a used item asks for confirmation and removes its clips.
- **Import.**
  - Ctrl+I (or the + button) opens a multi-select dialog, and OS drops also work on Windows. Known Linux limitation: Avalonia 11.3 has no X11 drag and drop from other apps (XDND), so file manager drops do nothing there and the empty bin points at Ctrl+I and +. Drags inside Cutlass work on every OS.
  - Folders import supported files up to two levels deep.
  - Unsupported files are listed in one toast.
- **Effects tab.**
  - Transitions: the eight video transitions plus Audio Crossfade. Drag one onto a cut, which highlights while hovered (video kinds on video tracks, Audio Crossfade on audio tracks). Double-click sets the default (star), which is remembered in settings. Crossfade is the factory default.
  - Title templates: Centered, Lower Third and Top Caption. Drag one onto a video track to create a 5 s title.
  - Fades: Fade In, Fade Out, Fade In and Out (1 s, video and audio) and Remove Fades. Apply acts on the selected clips; a drop acts on the clip under the pointer and, with linked selection on, its partners.

### 4.3 Viewer
- **Mode switch.** A segmented control switches Timeline and Source. Timeline is tinted VideoClip blue and Source Accent green, so the active monitor is never ambiguous.
- **Canvas and overlays.**
  - Pure black canvas, letterboxed.
  - Safe guides (G) at 90% and 80%.
  - While an exact frame decodes, a dimmed thumbnail placeholder is shown.
  - An amber "Dropped frames" badge appears after 2 s of lag. Its tooltip suggests lower quality or proxies.
- **Readout.**
  - Current timecode, 20 px semibold with tabular figures. Click it to type a timecode or a `+N`/`-N` frame offset.
  - Duration, shuttle speed (`2x`) and dropped-frame count.
- **Transport:** go to In, step back, play/pause, step forward, go to Out, loop.
- **Right side:** Quality (Auto, Full, Half, Quarter), proxy toggle (P), guides toggle.
- **Source mode adds:**
  - A mini ruler with In/Out handles. Click or drag it to scrub; drag a handle to move In or Out.
  - Mark In and Mark Out buttons, the In, Out and range duration readout, and Insert (`,`) and Overwrite (`.`) buttons.
  - "Video only" and "Audio only" drag handles that drag a single stream (unlinked; dropped on a track of the other kind it goes to the matching track of its own kind). Dragging the canvas drags both. A drop places the Source In/Out range like a bin drop: overwrite, Ctrl inserts, and the playhead stays.
- **Source monitor behaviour.**
  - The Source button is enabled once media is open. Source frames run at the media's nearest supported rate, from its first frame, and the canvas takes the media's display size.
  - While Source shows media, the transport and mark keys go to it: Space, Shift+Space, J, K, L, Left/Right (with Shift), Home/End, I, O, X (the whole media), Shift+I/O and Ctrl+Shift+X. Up and Down do nothing there. Every other key edits the sequence.
  - Pressing anywhere in the timeline panel, or starting a trim drag, shows the Timeline monitor again, so the keys always follow what the viewer shows. `,` and `.` work in both modes as long as media is open in Source; with nothing open they place the media selected in the bin.
  - After `,` or `.` the viewer stays in Source, so the next range can be marked and placed at once.
  - Each media item remembers its Source In, Out and playhead for the session. Opening one parks on its last playhead; match frame parks on the matching frame.
  - The click-to-type readout accepts `HH:MM:SS:FF` (shorter forms such as `12:04` or a bare frame count too) and `+`/`-` offsets in frames or as a timecode (`-00:00:01:00`). Enter seeks the monitor shown, Esc or clicking elsewhere cancels.
- **Trim feedback.**
  - Edge trims show the frame at the edited edge.
  - Roll shows a two-up of the outgoing (left) and incoming (right) frame.
  - Slip shows a two-up of the new first and last frame.

### 4.4 Inspector
Context sensitive, with collapsible sections whose state is remembered.
- **Media clip.**
  - Clip: name, file, enabled, speed %, source in/out.
  - Transform: Position X/Y (px from canvas center), Scale (% of fit), Crop L/T/R/B (% of source), Opacity %.
  - Color: Brightness (-1..1), Contrast (0..2), Saturation (0..3), Gamma (0.1..4), Reset.
  - Fades: video fade in and out (frames).
  - Audio: gain dB, fades, and a keyframe list (time, dB) with add and remove.
- **Title clip.**
  - Text (multi-line).
  - Font: bundled Inter Regular, SemiBold or Bold, or a system font file.
  - Size, color, outline width and color, box (on/off, color, padding).
  - Position preset (Center, Lower Third, Top) plus an X/Y offset.
  - Opacity and fades.
- **Transition:** kind and duration in frames. A red notice appears when the duration was clamped (whenever the transition sits at its limit, like the timeline's red corner flag).
- **Gap:** its length and "Close gap".
- **Nothing selected:** sequence settings. They are editable while the sequence is empty. Afterwards "Change..." opens a dialog that warns about rate conversion.
- **Several clips of one type:** only shared fields are shown. An edit applies to all of them as one undo step.

**ScrubbyNumberBox** (every numeric field):
- Drag the value box horizontally to change the value (the label sits beside it). Shift gives 10x steps, Ctrl 0.1x. Typed text also accepts a comma as the decimal separator.
- Double-click to type. Enter commits, Esc cancels.
- Right-click resets the value.
- One drag is one undo step. The viewer updates within about 50 ms.

### 4.5 Timeline panel, status bar, jobs
- **Toolbar (30 px):**
  - tools V, B, N, Y, C
  - snap magnet
  - linked-selection toggle (on by default)
  - editable playhead timecode
  - logarithmic zoom slider
  - "+ Track" menu
- **Track headers (150 px).**
  - Video: target chip (`V1`), name (double-click renames), eye, lock.
  - Audio: target chip, name, M, S, lock, and a dB gain field when the track is at least 48 px tall.
  - Target chips choose which tracks receive `,`/`.` and which tracks Ctrl+K splits when nothing is selected.
  - Context menu: rename, add track above or below, delete (confirms if the track is not empty).
- **Meters.** A stereo master peak meter sits at the timeline's right edge. Per-track meters (late milestone) are thin bars in the audio headers.
- **Status bar:**
  - ffmpeg state: green dot plus build id, or red "ffmpeg not found" linking to Settings
  - job summary
  - cache use against its limit
  - sequence format
  - "Audio unavailable" in amber when OpenAL failed or the device was lost during playback (re-checked after every play start, since a start may reopen the device)
- **Agent badge** (AGENT_INTEGRATION 6.3), a status bar segment after the job summary while agent connections are on: "Agent ready" (TextSecondary dot), "Agent: Claude Code" (Accent dot), "Agent paused" (Snap/Marker yellow dot) or "Agent waiting". A waiting agent also shows "Claude Code is waiting for the project". A click opens the AGENT tab.
- **AGENT tab** (AGENT_INTEGRATION 6.3). While connections are allowed the left panel has a third tab (MEDIA | EFFECTS | AGENT): a lock card (phase, agent, reason, lease left, Pause/Resume, Take over), a connection card (the server address, Copy token, Copy Claude Code command), the call log newest first (time, tool, agent, duration, code in Danger for errors, the arguments as a tooltip, at most 500 rows), and Open agent docs and Open log folder. View > Agent Panel opens it too; the View menu also has Agent: Take Over, Agent: Pause / Resume and Agent: Follow.
- **Jobs popover.** Clicking the job summary lists index, thumbnail, waveform, proxy and export jobs, each with progress, ETA, cancel and, on failure, "Details". A finished export names and opens the file it really wrote (`name (n).ext` when the target was locked) and lists its warnings (offline media, fallback fonts) in amber under the row. Probing runs inside the import, which the media card shows with its spinner (TECH_BRIEF 6.7).

---

## 5. Timeline interaction

### 5.1 Geometry and appearance
- **Coordinates.** `x = (frame - scrollFrame) * pixelsPerFrame`, with `pixelsPerFrame` from 0.01 to 40.
- **Sizes.**
  - Ruler 28 px.
  - Track height 56 px (video) and 48 px (audio) by default, adjustable from 32 to 160 px by dragging the header's bottom edge or with Alt+wheel.
- **Rendering.** One custom-drawn control that draws only the visible range.
- **Clip body.**
  - Rounded rectangle (3 px) in VideoClip, AudioClip, TitleClip or StillClip color.
  - Name in Inter 11 px at the top left.
  - Badges: speed (`150%`), `fx` (non-identity transform or color), and a red sync badge (`+5`) when linked clips have drifted.
- **Clip content.**
  - Video clips tile a filmstrip, each tile showing the source frame at its left edge.
  - Audio clips draw a min/max waveform from the peak level nearest one peak per pixel, overlaid by the volume line and keyframe dots. The height follows the square root of the normalised peak (`WaveformScale`), so quiet material stays readable: -20 dBFS fills about a third of the half height, full scale touches the edge.
- **Clip states.**
  - Fades: a diagonal shade plus 8x8 px corner handles.
  - Selected: 2 px white outline and a body 12% lighter.
  - Disabled: 40% opacity.
  - Offline (media whose file is missing, section 11): red stripes over the body and a red `OFFLINE` badge.
- **Drawing order:**
  1. lanes (locked tracks hatched)
  2. gaps (the selected one highlighted)
  3. clips
  4. transitions (hatched block centered on the cut)
  5. In/Out shading
  6. markers
  7. snap line
  8. playhead

### 5.2 Navigation, ruler, playhead

| Input | Action |
|---|---|
| Wheel, horizontal touchpad scroll | scroll time |
| Shift+wheel | scroll tracks vertically |
| Ctrl+wheel | zoom around the mouse |
| Alt+wheel | resize the hovered track |
| `+` / `-` | zoom around the playhead |
| Shift+Z | zoom to fit |

- **Playback follow.** During playback the view pages when the playhead leaves it.
- **Ruler labels** are `HH:MM:SS:FF` (non-drop). Ticks adapt from 1 frame to 5 min (further out at the smallest zoom); below one second the steps divide the second so labels land on round frames.
- **Scrubbing.** Click or drag in the ruler. With snapping on, the drag snaps to edit points. The playhead line can also be dragged where it crosses empty lane space.
- **Playhead.** A 1 px Playhead-red line with a pentagon head, always on a whole frame.
- **Lane clicks.** Clicking a lane with the Select tool selects. It never moves the playhead.

### 5.3 Selection
- **Click:** selects a clip and, with linked selection on, its link group.
- **Alt+click:** selects one side only.
- **Ctrl+click:** toggles a clip.
- **Shift+click:** adds a clip, never removes.
- **Drag on empty lane:** marquee.
- **Ctrl+A:** select all.
- **Esc:** cancels a drag, otherwise deselects.
- **Gaps and transitions.** A click without a drag on empty space between clips selects the gap. A click on a transition block selects it.
- **Undo.** Selection is not undoable. After undo or redo it is filtered to ids that still exist.

### 5.4 Hit zones and tools

**Hit zones,** highest priority first:
1. Keyframe dot (selected audio clips).
2. Fade handle: an 8x8 px corner square. Only when the clip is at least 40 px wide and the track at least 40 px tall.
3. Transition band: 40% of the track height, centered on a cut.
4. Edge: 6 px inside each end. Clips under 18 px wide split into thirds.
5. Volume line: within 4 px of it, on selected audio clips.
6. Body.

At a junction, each clip keeps its own edge zone. A press becomes a drag after 4 px of movement.

| Tool | Key | Body drag | Edge drag |
|---|---|---|---|
| Select | V | move (overwrite, Ctrl at drop = insert) | trim one side, leaves a gap; Ctrl+edge = ripple trim |
| Ripple | B | move | ripple trim: later clips on unlocked tracks shift |
| Roll | N | none | move the shared cut of two butting clips |
| Slip | Y | slip source in/out, clip stays put | same as body |
| Razor | C | click splits at the frame | same |

### 5.5 Editing behaviour
- **Overwrite** is the default drop. Covered clips are trimmed, split or removed.
- **Insert.** Ctrl held at drop inserts, and later clips on all unlocked tracks move right.
- **Alt+drag** on a body moves only that clip, not its partner.
- **Vertical moves** stay within one track kind. Linked partners follow by the same track offset when that track exists.
- **Locked tracks** cannot be selected or edited, and ripple or insert never shifts them. This can create sync badges.
- **Source drops** from the bin or Source mode create a video clip on the hovered video track and a linked audio clip on the matching audio track (same index, else A1).
- **Trim limits.** Trims stop at the source media limits (stills and titles have none) and, for plain trims, at the neighbor. Edits clamp instead of failing, and the tooltip shows the limit in red.
- **Ripple trim** (Ripple tool, Ctrl+edge, or Q/W) shifts everything after the edit point on all unlocked tracks.
- **Roll and slip.** Roll is limited by the handles on both sides. Slip never moves or resizes the clip; dragging right shows earlier source. A Roll-tool edge without a butting neighbour trims like the Select tool.
- **Linked trims.** A trim also trims a linked partner whose edge sits on the same frame. Alt at press time trims one side only, which is how J and L cuts are made.
- **Razor.**
  - Click splits the clip and its linked partners. Shift+click splits all unlocked tracks.
  - Ctrl+K splits the selection at the playhead, or the targeted unlocked tracks when nothing is selected. Ctrl+Shift+K splits all unlocked tracks.
  - The halves get new link groups: left halves together, right halves together.
- **Lift and ripple delete.**
  - Delete or Backspace lifts the selection, leaving a gap.
  - Shift+Delete ripple deletes. Tracks without room to close up are left alone. With nothing selected it ripple deletes the In/Out range.
  - With a gap selected, Delete, Shift+Delete, "Close gap" or right-click > Close gap all close it.
- **Snapping.**
  - On by default. S or the magnet toggles it.
  - Targets: playhead, all clip edges, markers, In/Out, frame 0.
  - The 8 px threshold is converted to frames at the current zoom.
  - An engaged snap draws a full-height Snap-yellow line. Shift during a drag suspends snapping.
- **Transitions.**
  - Ctrl+D adds the default video transition to the selected clips' cuts, or to the cut nearest the playhead on targeted tracks. Ctrl+Shift+D adds an audio crossfade.
  - Default duration is 1 s, always centered on the cut.
  - Durations clamp to both clip lengths and both source handles. A clamped transition gets a red corner flag and a tooltip such as "Shortened to 12 frames: not enough source after intro.mov".
  - Dragging either end of a transition band changes its duration by two frames per frame (it stays centered). Double-click selects it for the Inspector.
  - Editing a cut moves and re-clamps its transition. Inserting inside a transition removes it.
- **Fades and keyframes.**
  - Corner handles set fade lengths. Video fades go to transparent: black on V1, lower tracks show through above it. Audio fades are linear.
  - On selected audio clips:
    - Dragging the volume line changes gain (Shift for fine steps).
    - Ctrl+click or double-click on the volume line adds a keyframe; Alt+click on a keyframe removes it.
    - Between keyframes, dragging the line moves both keys of that segment; without keyframes it changes the clip gain.
    - Keyframes drag in dB and in time between their neighbors.
    - Delete removes the selected keyframe. Shift during a keyframe drag keeps it on one axis (time or level).
  - The scale runs from -60 dB to +12 dB, with 0 dB at 83% of the clip height.
- **Markers and In/Out.**
  - M adds a marker. Markers drag. Double-click edits name and color. A click on a marker head moves the playhead to it.
  - Shift+M and Ctrl+Shift+M jump to the next and previous marker.
  - I and O set In and Out. O includes the frame under the playhead (Out is exclusive), and an In at or after the Out clears the Out (an Out at or before the In clears the In). X marks the clip under the playhead. Ctrl+Shift+X clears.
  - The range is shaded with the InOutRange token.
- **Linked clips.**
  - A link is only a group id, used for selection and moving. Linked clips do not have to share start or duration.
  - Ctrl+L links or unlinks.
  - Drifted pairs show sync badges, whose right-click offers "Move into sync".
- **Copy and paste.**
  - Ctrl+C copies clips with relative offsets and tracks. Ctrl+X also lifts.
  - Ctrl+V pastes at the playhead in overwrite mode on the same tracks. Ctrl+Shift+V pastes in insert mode. The playhead then moves to the end of the pasted clips.
  - Pasted clips get new ids and link groups.
- **Three-point editing.**
  - `,` and `.` place the Source In/Out range (or the whole media) on the topmost targeted video and audio tracks, at the timeline In if set, else at the playhead. The playhead then moves to the end of the placed clips.
  - With no Source In/Out but a timeline In and Out, the timeline range sets the duration.
  - F (match frame) opens the clip under the playhead in Source mode at the matching frame.
- **Speed.** Ctrl+R opens a dialog: 25 to 400% plus "Ripple following clips" (on by default). The source range is kept and the duration changes.

### 5.6 Drag feedback
- **Live result.** The timeline redraws the computed result, with a faint outline of the original position.
- **Tooltip.** It shows the delta (`+00:00:01:12`) or the new duration (`Dur 00:00:04:03`).
- **Snapping.** The snap line shows while a snap is engaged.
- **Esc** cancels. Release commits exactly one undo step. Only releasing the left button commits; another button does nothing.
- **Keys during a drag.** S (snapping) and the zoom keys work, and Ctrl, Shift and Alt change the drag. Every other shortcut is ignored until the drag ends, so nothing edits the project under it.
- **Playback.** Starting a drag pauses playback.

---

## 6. Playback (user view)

- **Space** plays or pauses. **Shift+Space** plays In to Out. **Ctrl+Shift+L** toggles loop.
- **Shuttle.**
  - L plays forward, and repeated presses give 2x and 4x. Audio is muted above 1x.
  - K pauses. Holding K and tapping J or L steps one frame.
  - J is reverse shuttle (late milestone). Until then it steps back one frame.
- **Quality.** Auto is the viewer's physical size, capped at 960x540 while playing and 1920x1080 when paused. Full, Half and Quarter are relative to the sequence.
- **Proxies.** These are preview-only intra-frame files. The Auto policy builds one for sources taller than 1080 lines, HEVC, AV1 or VP9, VFR, or above 50 Mb/s. P toggles their use. Export always reads the originals.
- **Live changes.** Mute, solo and track gain apply within about 150 ms without a restart. Timeline edits restart playback at the current frame after 150 ms of edit quiet.
- **No audio device.** Playback uses a silent clock and the status bar says "Audio unavailable". A device lost during playback shows the same amber badge in the viewer, next to the "Dropped frames" badge, until a later play start reopens it.

---

## 7. Keyboard shortcuts

The defaults live in a static keymap. Non-letter keys are named by their Avalonia `Key` value so they work on QWERTY and QWERTZ.

| Key | Action | Key | Action |
|---|---|---|---|
| Space | play / pause | Shift+Space | play In to Out |
| L | play forward, again for 2x, 4x | K | pause |
| J | reverse shuttle (late; until then 1 frame back) | K held + J / L | 1 frame back / forward |
| Left / Right | 1 frame | Shift+Left / Right | 10 frames |
| Up / Down | previous / next edit point | Home / End | sequence start / end |
| I / O | mark In / Out | Ctrl+Shift+X | clear In / Out |
| X | mark clip under playhead | Shift+I / Shift+O | go to In / Out |
| M | add marker | Shift+M / Ctrl+Shift+M | next / previous marker |
| V B N Y C | Select, Ripple, Roll, Slip, Razor | S | toggle snapping |
| Ctrl+K | split at playhead | Ctrl+Shift+K | split all unlocked tracks |
| Delete, Backspace | lift (or close selected gap) | Shift+Delete | ripple delete |
| Q / W | ripple trim start / end to playhead | Alt+Left / Right | nudge selection 1 frame |
| `,` OemComma / `.` OemPeriod | insert / overwrite from Source | F | match frame |
| Ctrl+D | default video transition | Ctrl+Shift+D | default audio crossfade |
| Ctrl+T | new title at playhead | Ctrl+R | speed dialog |
| Ctrl+L | link / unlink | Shift+E | enable / disable clip |
| Ctrl+C / Ctrl+X | copy / cut | Ctrl+V / Ctrl+Shift+V | paste overwrite / insert |
| Ctrl+A | select all | Esc | cancel drag, else deselect |
| Ctrl+Z | undo | Ctrl+Shift+Z, Ctrl+Y | redo |
| `+` OemPlus or Add / `-` OemMinus or Subtract | zoom in / out | Shift+Z | zoom to fit |
| G | safe guides | P | toggle proxies |
| Ctrl+Shift+L | loop | Ctrl+, | settings |
| Ctrl+N / Ctrl+O | new / open | Ctrl+S / Ctrl+Shift+S | save / save as |
| Ctrl+I | import | Ctrl+E | export |
| Ctrl+Q | quit | | |

**While an agent holds the project** (AGENT_INTEGRATION 3.7 and 3.8) only these keys run: transport (Space, Shift+Space, J, K, L) and loop, the playhead steps, edit points, Home and End, go to In/Out, marker navigation, zoom, guides, proxies, snapping, the tool keys, match frame, Ctrl+A, Ctrl+C, Esc, Ctrl+S, Ctrl+Shift+S, Ctrl+E, Ctrl+, and Ctrl+Q. I, O, X and Ctrl+Shift+X run only while they target the Source monitor (Source In/Out are view state). Every other bound key is claimed and dropped with the toast "An agent is editing. Take over to edit.", and its menu item is disabled. Edit > Undo and Redo name the step, for example "Undo Agent: Remove silences"; one Ctrl+Z after Take over reverts a whole agent batch.

**Focus rule.** While a text input has focus, keys go to it except Ctrl+S, Ctrl+E, Ctrl+N, Ctrl+O and Esc. Esc goes to the input first (an inspector field restores its value, a scrubby field closes its editor) and runs the app's Esc only when the input does not use it. On macOS Ctrl maps to Cmd.

---

## 8. Visual style

Dark, neutral and low-chroma, so footage colors read correctly. The theme is Fluent with `RequestedThemeVariant="Dark"`, with resources overridden in `Themes/CutlassTheme.axaml`.

| Token | Color | Use |
|---|---|---|
| Bg0 | `#141518` | window chrome, timeline lanes |
| Bg1 | `#1C1E22` | panels |
| Bg2 | `#25282D` | headers, track headers, toolbars |
| Bg3 | `#30343A` | hover, inputs |
| Border | `#2F3238` | 1 px separators |
| TextPrimary | `#E6E8EB` | text |
| TextSecondary | `#9BA1A9` | labels, ruler |
| TextDisabled | `#5C626B` | disabled |
| Accent | `#4CC38A` | active tool, focus ring, tab underline, primary button, Source pill |
| Warning | `#F5A524` | amber warnings: HDR and CHANGED badges, Dropped frames, Audio unavailable, job warnings |
| Playhead | `#FF4D4D` | playhead |
| Selection | `#FFFFFF` | clip outline |
| VideoClip | `#3A6EA5` | video clips, Timeline pill |
| AudioClip | `#2E8B6E` | audio clips |
| TitleClip | `#8E5BD6` | titles |
| StillClip | `#4F7C9A` | images |
| Snap / Marker | `#F5D547` | snap line, default marker |
| InOutRange | `#FFFFFF` at 8% | In/Out shading |
| Danger | `#E5484D` | errors, offline, sync badge, clamp flag |
| Success | `#3DDC84` | ffmpeg ok |
| Meter | `#3DDC84` to -6 dBFS, `#F5D547` to -1 dBFS, `#FF4D4D` above | meters |

**App icon.** `Assets/cutlass.ico` (16 to 256 px): a rounded Bg2-to-Bg0 tile with a VideoClip and an AudioClip bar, cut diagonally by a cutlass with an Accent green blade, a TextPrimary guard and knuckle bow and a TextSecondary grip. At 24 px and below the bow is left out and the shapes are drawn thicker.

**Typography.** Inter via `Avalonia.Fonts.Inter` and `.WithInterFont()`.
- Body text 12 px.
- Panel titles 11 px semibold uppercase, +4% letter spacing.
- Clip labels 11 px, ruler 10 px.
- Viewer timecode 20 px semibold, toolbar timecode 13 px semibold.
- Timecodes use the `tnum` font feature. If it is not honored, digits are laid out in fixed-width cells.

**Spacing.**
- 4 px grid, 30 px toolbars, 26x26 icon buttons.
- Radius 3 px on clips and 4 px on controls.
- Monochrome `PathIcon` geometries in `Themes/Icons.axaml` (no icon packages).
- 1 px Accent focus rings.
- A dense layout, like an editing suite.

---

## 9. Export

**Export dialog (Ctrl+E):**
- **Preset.** Unavailable ones are greyed out with a reason such as "libx265 is not in this ffmpeg build".
- **Quality:** Draft, Good, High or Best.
- **Resolution:** Sequence, 2160p, 1080p, 720p or custom (even, aspect locked). GIF picks a width instead.
  - Custom takes a height of 16 to 4320 lines, rounded down to even; the width follows the sequence aspect, rounded to even and at most 8192, and is shown next to the field.
- **Range:** Whole, or In to Out.
- **Output path** with Browse. The extension follows the preset. It must be a full path with a valid file name and must not be a media file of the project (compared by spelling and, when the output exists, by file identity, so links and other spellings of a media file are refused too); an existing file is replaced only after a confirmation.
- **Limit peaks** (on by default).
- **Estimated size:** exact for fixed bitrates (PCM, AAC, MP3, ProRes, OpenH264), "Roughly ... (depends on the content)" for CRF video from typical bits per pixel.
- **Show ffmpeg command,** with Copy. It is built like the export itself, with the title fonts resolved.
- **Export** queues the job, and the Jobs popover shows its progress. The dialog starts with the folder and preset from Settings and stores those of each export there.

**Export behaviour.**
- One export runs at a time.
- A running proxy job is cancelled and re-queued during an export.
- Output goes to `name.partial.ext` and is renamed on success.
- Failures open an error dialog with the summary, the last 30 ffmpeg log lines and "Copy details" (which includes the command).

| Preset | Container | Video | Audio | Needs |
|---|---|---|---|---|
| H.264 (default) | .mp4 | x264 CRF (fallback OpenH264 bitrate) | AAC 192k | libx264 or libopenh264 |
| H.265 | .mp4 | x265 CRF, `hvc1` tag | AAC 192k | libx265 |
| VP9 | .webm | libvpx-vp9 constant quality | Opus 160k (fallback Vorbis) | libvpx-vp9 |
| AV1 | .mp4 | SVT-AV1 (fallback libaom) | AAC 192k | libsvtav1 or libaom-av1 |
| ProRes 422 HQ | .mov | prores_ks profile 3, 10-bit 4:2:2 | PCM s16 | prores_ks |
| GIF | .gif | 15 fps palette, width 320/480/640/800 | none | palettegen, paletteuse |
| MP3 / AAC / WAV / FLAC | .mp3 / .m4a / .wav / .flac | none | LAME VBR / AAC 256k / PCM / FLAC | libmp3lame / aac / always / always |

Exact flags are in TECH_BRIEF section 9.

---

## 10. Settings

Settings (Ctrl+, or Edit > Settings) has five pages: General, Media, Playback, Agents and About. OK and Apply validate the fields (whole minutes and GB, seconds with a point, full paths; the first problem shows under the pages and disables both) and save `settings.json` atomically; Cancel drops what was not applied. Changes apply live: the cache limit (an LRU pass when it shrinks), the proxy policy (the open project's media are queued again), the preview defaults, the default durations, the track confirmation and the autosave interval. A new audio device applies on the next start, and so does a new ffmpeg while a working one runs (without one it is used at once).
- **General:** autosave interval (2 min), default transition durations (1 s), default still and title duration (5 s), confirm before deleting tracks, the export folder (empty for the project's folder) and preset, which the export dialog starts with and updates on every export, Clear recent projects, and a note that the theme is dark only.
- **Media:**
  - ffmpeg location (the executable or its folder, empty for the PATH search), with detected status ("found: N-126308, libavfilter 12"), Re-detect (runs `-version` and the capability probe off the UI thread), Browse and Use PATH. A typed location is checked before it is saved.
  - Proxy policy: Auto, Always or Never.
  - Cache folder and size limit (20 GB), with Clear cache (keeps only what a running job uses; the open project's posters, thumbnails and waveforms are rebuilt in the background).
  - The cache folder is read-only, with Open folder and the current use.
- **Playback:** default preview quality, audio output device ("System default" plus the OpenAL device list), use proxies.
- **Agents** (AGENT_INTEGRATION 6.1):
  - "Allow agent connections" (off by default) with a status line: "Listening on http://localhost:47390/" (Success dot), "Off", "Another Cutlass window serves agents" or "Port 47390 is in use" (Danger). Started with `--agent-server` the switch shows "On for this session (--agent-server)" and the saved value is unchanged.
  - Port (47390, 1024 to 65535; when it is busy the next nine ports are tried), lease timeout (90 s, 15 to 600) and the longest lease an agent may ask for (300 s, 15 to 600), "Ask me before an agent takes the project" (off).
  - The token, masked, with Copy and "Regenerate token..." (confirms first).
  - "Agent may read from" and "Agent may write to" folder lists with Add (folder picker) and Remove; the project's and its media's folders are always readable, the export folder (or the project's folder) always writable; a long path ends in an ellipsis and shows in full as a tooltip.
  - MCP setup: the Claude Code command (`claude mcp add cutlass -- <exe>`) and the Claude Desktop JSON with the real path of `Cutlass.Agent.Mcp`, each with Copy, and "Open agent docs" (`AGENT_INTEGRATION.md` beside the exe).
- **About:** versions, the ffmpeg build id and path, (Inter is under the SIL OFL) and project links as plain text. Help > About Cutlass opens this page; Help also has Open Log Folder (`<data>/logs`) and Locate ffmpeg.

**First-run help.** When ffmpeg is missing, the install hints are `winget install Gyan.FFmpeg` (Windows), `sudo apt install ffmpeg` (Debian/Ubuntu) and `sudo dnf install ffmpeg` (Fedora with RPM Fusion), plus a manual Locate button.

---

## 11. Project files, autosave, recovery, relink

- **Project file and sidecar.** `.cutlass` holds media, probe data and the sequence. View state (zoom, scroll, playhead, track heights, viewer quality, bin view) lives in `<name>.cutlass.user`, so navigation never dirties the project.
- **Atomic save.** Save writes a temp file, flushes it and renames it over the old one.
- **Autosave.**
  - Runs 30 s after the last edit while the project is dirty, at most once per interval. Continuous editing still autosaves once the oldest unsaved edit is one interval old. Never during a drag or gesture. An interval of 0 turns autosave off.
  - Writes `<name>.cutlass.autosave`. Untitled projects autosave to the data folder (`autosave/untitled-<id>.cutlass`).
  - If an autosave is newer than its project on open, Cutlass offers Recover, Discard or Not now. A recovered project opens unsaved (dirty).
  - At startup the newest leftover autosave is offered the same way, and every leftover autosave (untitled ones and those of recent projects) is listed in the welcome overlay with Recover and Discard.
  - Clean saves and closes delete it. A crash writes one last autosave and logs the error to `logs/` in the data folder; on Linux so do SIGTERM, SIGHUP and SIGINT (logout, a closed terminal).
  - A running Cutlass marks its autosaves with a `<autosave>.lock` owner file, so a second instance never offers another running instance's autosave for recovery or discard.
- **Relink.**
  - On open, media is looked up by absolute path, then by its path relative to the project (found that way, it is relinked silently).
  - Changed files (size or mtime) are re-probed and fingerprinted; when the content changed they are updated in one undo step and get a "changed" badge. A file that was only touched counts as online.
  - Missing files are offline: striped in the bin, hatched on the timeline with the `OFFLINE` badge, and their clips stay. Nothing touches the path of media that were offline when the project opened (no file check, poster, index, thumbnail, waveform or proxy job) until they are relinked or confirmed.
  - A path on a remote volume other than the project's (a UNC path or network drive on Windows, a network file system on Linux) is not looked up on open, since touching it can send the user's credentials to that server. Such media count as offline, and the Relink dialog marks them "Not checked" with a "Confirm location" button that looks the recorded path up; a file found there is relinked with Relink like a located one.
  - The Relink dialog (File > Relink media..., or Relink... on a bin item) offers "Search folder..." (recursive; match by name, then by name without extension, then size plus fingerprint) and per-item "Locate...", which also resolves other missing files found by name in the same folder. Relink re-probes the new files and replaces the media in one undo step; clips are trimmed when a new source is shorter.

---

**TLDR:** Cutlass v1 is a dark, Premiere-style editor with a Media/Effects panel, a two-mode viewer, a context Inspector with scrubby fields and a custom-drawn timeline. The timeline has 8+8 tracks and five tools, overwrite and insert edits, snapping, transitions, fades, volume keyframes, titles, PiP and color. Links are only selection groups, so split edits work. The shortcut table has no conflicts and works on any keyboard layout. Playback supports JKL forward with live mute and solo. Reverse shuttle, per-track meters, hardware encoders and segmented export are late items. Ten export presets are gated on what the ffmpeg build contains. View state lives in a sidecar, and saves are atomic with autosave and relink.
