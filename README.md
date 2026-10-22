# Cutlass

Cutlass is a small, fast non-linear video editor for Windows and Linux, built with .NET 10 and Avalonia 11.3.22. It does all media work by running your own `ffmpeg` and `ffprobe`, and it is frame exact: scrubbing, playback and export all come from one timeline compiler, so what you see is what you export.

> Screenshot placeholder: a screenshot of the main window goes here. Until then, `build/smoke.ps1` writes current screenshots of the main window and the dialogs to `<OutDir>/shots`.

## Features

- **Import.** Video, audio and stills through the file dialog (Ctrl+I), drops onto the Media panel or drops straight onto the timeline (file manager drops work on Windows; see the Linux notes). Probing, posters, thumbnails, waveforms and proxies run in the background.
- **Timeline editing.** Up to 8 video and 8 audio tracks with the Select, Ripple, Roll, Slip and Razor tools, overwrite and insert edits, trim, split, lift, ripple delete, gap close, snapping, markers, In/Out, linked audio and video, copy and paste, three-point edits from the Source monitor, track lock, hide, mute, solo and gain, and undo up to 500 steps.
- **Transitions.** Crossfade, Dip to Black, Dip to White, Wipe Left and Right, Slide Left and Right, Circle Open, and Audio Crossfade, plus fade handles on every clip.
- **Titles.** Centered, Lower Third and Top Caption text titles.
- **Speed.** Constant speed from 25% to 400% with the pitch kept.
- **Volume keyframes.** Clip gain and a keyframed volume envelope drawn on audio clips.
- **Picture in picture and color.** Position, scale, crop, opacity, brightness, contrast, saturation and gamma.
- **Proxies.** Created automatically for heavy media (Auto policy) or on request, used for preview only.
- **Playback with audio.** Frame accurate scrub and step, real-time playback through OpenAL with J/K/L shuttle at 1x, 2x and 4x, loop In to Out, a master meter and a dropped-frame indicator.
- **Export presets.** H.264, H.265, VP9, AV1, ProRes 422 HQ, GIF, MP3, AAC, WAV and FLAC, for the whole sequence or In to Out, with quality and resolution options, a job queue with progress and cancel, and "Show ffmpeg command". Presets the ffmpeg build cannot encode are greyed out with the reason.
- **Never lose work.** Atomic saves, autosave, crash recovery, recent projects and relinking of moved or missing media (File > Relink media...).

## Requirements

- **ffmpeg 6.0 or newer** with `ffprobe`, either on the `PATH` or chosen in Settings (Ctrl+, then Media, "ffmpeg location"). ffmpeg is a separate install and is not bundled with Cutlass. Install hints:
  - Windows: `winget install Gyan.FFmpeg`
  - Debian and Ubuntu: `sudo apt install ffmpeg`
  - Fedora (with RPM Fusion): `sudo dnf install ffmpeg`
- **.NET 10 runtime**, unless you use a self-contained publish (see below), which carries its own runtime. Building needs the .NET 10 SDK.

## Build, run and test

```
dotnet build Cutlass.slnx --disable-build-servers -m:1
dotnet run --project src/Cutlass.App
dotnet run --project src/Cutlass.App -- <project.cutlass>
dotnet run --project src/Cutlass.App -- --import <file>...
```

- Unit tests (no ffmpeg needed): `dotnet test tests/Cutlass.Tests --disable-build-servers -m:1`
- Integration tests (need ffmpeg; they skip themselves without it): `dotnet test tests/Cutlass.IntegrationTests --disable-build-servers -m:1`
- Refresh the compiler goldens: set `CUTLASS_UPDATE_GOLDEN=1`, run the unit tests, then review the diff.
- Text check (no em dashes and similar rules): `powershell -File build/check-text.ps1`

## Smoke tests

The smoke tests run a script inside the real app against freshly generated lavfi clips. They need ffmpeg and a desktop session.

```
pwsh build/smoke.ps1 [-Script m4|m5|m6|m7|m7b] [-OutDir <folder>] [-AppPath <published exe>]
powershell -File build/smoke.ps1 -Script m7
```

- `m4` (default): import, cut, scrub, undo, save and reopen, export and an ffprobe check.
- `m5`: media jobs, proxies, playback drift, live mute and solo, and 2x/4x shuttle.
- `m6`: Source mode, three-point edits, match frame, copy and paste, Q/W, transitions, speed and export, plus the timeline checklist screenshots.
- `m7`: the Settings dialog and one export per preset, each checked with ffprobe.
- `m7b`: offline media on the timeline and screenshots of the Relink, Settings, Export, Speed and Sequence Settings dialogs.

CI runs `m4` on Windows and Linux (under `xvfb-run`) and `m7` on Windows.

A run exits with 0 when every step passed, writes its log next to the script copy in `<OutDir>` and restores the settings it started with. The script commands are listed in `src/Cutlass.App/Services/SmokeScript.cs`.

## Publish

Self-contained, one folder, into `artifacts/publish/<rid>`. The script publishes `Cutlass.App` and the MCP server `Cutlass.Agent.Mcp` side by side (it fails when the two would need different versions of a shared file), copies `docs/AGENT_INTEGRATION.md` beside the exes, and checks that the folder holds both exes, the OpenAL native for the runtime and the Inter fonts. When the runtime matches the OS, it also runs `Cutlass.Agent.Mcp --print-registration` on the published exe.

```
powershell -File build/publish.ps1 [-Runtime win-x64|linux-x64]
bash build/publish.sh [linux-x64|win-x64]
dotnet publish src/Cutlass.App -c Release -r linux-x64 --self-contained
```

Run the shell script through `bash`: a checkout made on Windows (`core.filemode=false`) does not carry its execute bit. When the script is first committed, mark it executable in the index with `git update-index --chmod=+x build/publish.sh`.

To check a Windows publish end to end, run the M4 smoke test on it: `powershell -File build/smoke.ps1 -AppPath artifacts/publish/win-x64/Cutlass.App.exe`. A linux-x64 publish can be made on Windows but runs only on Linux.

## AI agents

Cutlass can be edited by an LLM agent such as Claude Code, Claude Desktop, Cursor or a script. The agent brings its own model; Cutlass hosts the tools: inspect the timeline, look at frames, measure loudness, find silences, place, trim, split and remove ranges, add titles, transitions and markers, and export. Every agent change lands in the normal undo history as a named step ("Agent: Remove silences"), so one Ctrl+Z reverts a whole agent operation. The full specification is `docs/AGENT_INTEGRATION.md` (copied beside the exes by the publish scripts).

There are two surfaces over one engine:
- **The local HTTP API**, hosted by the running app on loopback. Turn it on in Settings > Agents > "Allow agent connections", or for one run with `Cutlass.App --agent-server [--port N]`.
- **The MCP server** `Cutlass.Agent.Mcp`, published next to `Cutlass.App`. By default it proxies to the running app, so you watch the agent work in the timeline and viewer. With `--headless` it hosts the engine itself without a window.

**The edit lock.** An agent must take the edit lock before it changes anything. While it holds the lock, a banner names the agent and its reason and the editing UI is read-only; playback, scrubbing, Save and Export stay available. "Pause" makes the agent's next change wait, and "Take over" ends the lock at once and makes the UI editable again. The lock is a 90 s lease renewed by the agent's calls, so a stuck or vanished agent loses it on its own.

### Registering the MCP server

Print the exact commands for the exe you have: `Cutlass.Agent.Mcp --print-registration`.

Claude Code (add `--scope user` to register it for every project):

```
claude mcp add cutlass -- "C:\Program Files\Cutlass\Cutlass.Agent.Mcp.exe"
claude mcp add cutlass -- /opt/cutlass/Cutlass.Agent.Mcp
```

During development use the built exe, `src\Cutlass.Agent.Mcp\bin\Debug\net10.0\Cutlass.Agent.Mcp.exe`.

Claude Desktop (`claude_desktop_config.json`); other clients take the same command and arguments:

```json
{ "mcpServers": { "cutlass": { "command": "C:\\Program Files\\Cutlass\\Cutlass.Agent.Mcp.exe", "args": [] } } }
```

Then ask, for example: "In Cutlass, cut the silence out of the interview and add a title 'Meet Ana' for the first 4 seconds."

### Headless and scripted use

MCP with the engine in process (no app, no window), for a project file:

```
claude mcp add cutlass-headless -- "C:\Program Files\Cutlass\Cutlass.Agent.Mcp.exe" --headless --project "D:\Edits\ep07.cutlass" --allow-read "D:\Footage" --allow-write "D:\renders"
```

`--create` makes the project file when it is missing. On exit with unsaved changes the headless host writes an autosave that the app offers to recover; it never overwrites the project without an explicit save.

The HTTP API without MCP, for scripts and render farms:

```
Cutlass.Agent.Mcp serve --project ep07.cutlass --port 47410 --allow-write D:\renders
```

It prints the base URL and the token file and runs until `POST /v1/shutdown` or Ctrl+C. A script then calls `POST /v1/lock/acquire`, `POST /v1/exports`, polls `GET /v1/exports/{id}?waitMs=20000` and finishes with `POST /v1/shutdown {"save": true}` (a locked call, so it still holds the lock or takes it again after `/v1/lock/release`), each request with `Authorization: Bearer <token>`.

`build/examples/agent-serve-demo.ps1` does all of this with `Invoke-RestMethod`: it generates a lavfi clip, starts `serve` on a new project, imports and places the clip, exports H.264, releases, shuts the server down with a save, and checks the export with ffprobe (`powershell -File build/examples/agent-serve-demo.ps1 [-McpExe <exe>] [-Port N]`).

`--headless` and `serve` refuse a project that a running Cutlass serving agents has open, because two writers would overwrite each other's saves; use the default proxy mode for that project instead.

### Security

- **Off by default.** The app serves agents only when you allow it in Settings or start it with `--agent-server`.
- **Loopback only.** The API listens on `localhost` (Windows) or `127.0.0.1` (Linux) and refuses non-loopback peers, foreign `Host` headers and any request with an `Origin` header (browsers).
- **Bearer token.** Every request needs the token from `<data>/agent/token` (owner-only on Linux, the per-user profile on Windows). "Regenerate token" in Settings > Agents replaces it.
- **Folders.** An agent reads only the project's and its media's folders plus the folders you add under "Agent may read from" (or `--allow-read`), and writes only to the export folder plus "Agent may write to" (or `--allow-write`). The data, cache and settings folders are always refused.
- **Audit log.** Every call is logged with time, agent, tool, duration and result in the Agent panel and in `<data>/logs` (tokens and bodies never).
- **Project content is data.** Titles and file names reach the model; the server instructions tell it to treat them as data, and no tool executes content.

## Linux notes

- The OpenAL Soft native (`libopenal.so`) is bundled in the published folder, so preview audio needs no system OpenAL package.
- Titles use the bundled Inter fonts; for system fonts Cutlass asks fontconfig (`fc-match`) and falls back to DejaVu Sans.
- Settings live in `${XDG_CONFIG_HOME:-~/.config}/cutlass/`, data (recent projects, autosaves, logs) in `${XDG_DATA_HOME:-~/.local/share}/cutlass/` and the media cache in `${XDG_CACHE_HOME:-~/.cache}/cutlass/`.
- Dropping files from a file manager does not work on Linux yet: Avalonia 11.3's X11 backend has no XDND support. Import with Ctrl+I, the + button in the Media panel or "Import media..." on the welcome screen. Drags inside Cutlass (media, Source ranges and effects onto the timeline) work.
- A crash, logout or closed terminal (SIGTERM, SIGHUP, SIGINT) still writes one last autosave of unsaved changes.
- Cutlass runs on X11 and XWayland. Smoke tests need a display; on a headless machine run them under `xvfb-run`, for example `xvfb-run -a pwsh build/smoke.ps1 -Script m4`.

## Known limitations

- Linux has never been run on real hardware; every Linux path was verified by reading only. CI has never executed on GitHub.
- Avalonia 11.3.22 on X11 has no XDND, so OS file drops do nothing on Linux; use Ctrl+I or the Import button there.
- Two instances sharing one cache can evict each other's proxies; the app detaches and re-queues lost proxies but does not lease artifacts.
- atempo alignment is calibrated with tone bursts (mean within 1 ms); a single click cannot be held to 1 ms by any constant compensation.
- The late (M8) items are not built: reverse shuttle (J steps back one frame), per-track meters, hardware encoders, segmented export for very large timelines.

See `docs/STATUS.md` for the milestone table, test counts and what was verified where.

## Licences

- The Inter typeface is bundled under the SIL Open Font License 1.1 (`fonts/OFL.txt` next to the app).
- Avalonia, CommunityToolkit.Mvvm, Silk.NET, the ModelContextProtocol C# SDK and Microsoft.Extensions.Hosting are used under the MIT License; OpenAL Soft is used under the LGPL.
- ffmpeg is not bundled: Cutlass runs the ffmpeg and ffprobe you install, under their own licence.

## Documentation

- `docs/DESIGN.md`: product scope, UX, timeline behaviour, shortcuts, visual style and export presets.
- `docs/TECH_BRIEF.md`: architecture, file map, compiler rules, ffmpeg commands, threading, testing and the build order.
- `docs/AGENT_INTEGRATION.md`: the agent API, the edit lock, the MCP server and its registration.
- `docs/STATUS.md`: milestone status, test counts, smoke coverage and known limitations.
- `CLAUDE.md`: the short working guide (commands, invariants and conventions).
