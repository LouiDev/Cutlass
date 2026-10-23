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

CI runs `m4` on Windows and Linux (under `xvfb-run`), `m7` on Windows, and `build/examples/agent-http.ps1` against `Cutlass.Agent.Mcp serve` on Windows.

A run exits with 0 when every step passed, writes its log next to the script copy in `<OutDir>` and restores the settings it started with. The script commands are listed in `src/Cutlass.App/Services/Smoke/SmokeScript.cs`.

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

Any LLM agent that can make tool calls can edit in Cutlass: an MCP client, an OpenAI-compatible model in a short tool loop, the Anthropic or another vendor SDK, or a plain script. The agent brings its own model; Cutlass hosts the tools: inspect the timeline, look at frames, measure loudness, find silences, place, trim, split and remove ranges, add titles, transitions and markers, and export. Every agent change lands in the normal undo history as a named step ("Agent: Remove silences"), so one Ctrl+Z reverts a whole agent operation. The full specification is `docs/AGENT_INTEGRATION.md` (copied beside the exes by the publish scripts); its section 9 covers provider neutrality.

### The generic contract

Everything a client needs is derived from one tool catalog and served in a vendor-neutral form on the local HTTP API (`http://localhost:<port>/`, every request with `Authorization: Bearer <token>`):

1. **Tool definitions in your format:** `GET /v1/tools?format=openai` (the function-calling shape that OpenAI, Azure OpenAI, Ollama, LM Studio, vLLM, Groq, Mistral and DeepSeek accept), `format=anthropic`, `format=gemini`, `format=markdown` (a readable reference), plain `/v1/tools` (the catalog with lock and result kinds), or `GET /v1/openapi.json` (OpenAPI 3.1 for LangChain, Semantic Kernel, CrewAI, GPT-style actions or Postman).
2. **One call route:** `POST /v1/call` with `{"tool": "cutlass_split", "args": {"frame": 120}}`. The reply is the envelope `{revision, warnings, result}` or `{revision, error: {code, message, details, retryAfterMs}}`; image tools answer with `pngBase64`. After `cutlass_acquire_lock`, send its `lockId` as `X-Cutlass-Lock` on every call. The per-route REST endpoints (`POST /v1/edit/split`, ...) stay for scripts.
3. **The workflow text:** `GET /v1/instructions` (lock, describe, batch, verify, release), the same rules the MCP server sends as its instructions and the briefing renders.

MCP clients get the same tools, names and rules from `Cutlass.Agent.Mcp`, which proxies to the running app or, with `--headless`, hosts the engine itself.

**Where the API comes from.** The running app serves it once you allow it: **Agent > Start MCP server** (F9) for this session, Settings > Agents > "Allow agent connections" for good, or `Cutlass.App --agent-server [--port N]`. You then watch the agent work in the timeline and viewer. Without a window, `Cutlass.Agent.Mcp serve --project <file>` serves the same API headless. Both write `<data>/agent/endpoint.json` (base URL, pid, token file), which the examples and the MCP proxy read.

### Clients

**Agent > Connect an agent client...** lists the clients it detects first, each with Register / Update / Remove (or Copy for the text-only rows); Settings > Agents > "Connect clients" holds the same list plus the discovery URLs. **Agent > Copy briefing for your agent** has one item per client: paste the text into the agent session and it knows what is open and how to work. `Cutlass.Agent.Mcp --print-registration` prints every client's registration (command or config snippet) for the exe you have. The rows come from `agent-clients.json` beside the exe; your own profiles in `<data>/agent/clients/*.json` add to or override them.

| Client | Profile id | Transport | How to connect (the menu does it for you; this is the manual equivalent) |
|---|---|---|---|
| Claude Code | `claude-code` | MCP stdio | `claude mcp add cutlass --scope user -- "<exe>"` |
| Claude Desktop | `claude-desktop` | MCP stdio | `mcpServers.cutlass` in `claude_desktop_config.json` (Windows `%APPDATA%\Claude`, Linux `~/.config/Claude`), then restart it |
| Cursor | `cursor` | MCP stdio | `mcpServers.cutlass` in `~/.cursor/mcp.json` |
| Windsurf | `windsurf` | MCP stdio | `mcpServers.cutlass` in `~/.codeium/windsurf/mcp_config.json` |
| VS Code (Copilot agent mode) | `vscode` | MCP stdio | `servers.cutlass` (`"type": "stdio"`) in the user `mcp.json` (Windows `%APPDATA%\Code\User`, Linux `~/.config/Code/User`) |
| Gemini CLI | `gemini-cli` | MCP stdio | `mcpServers.cutlass` in `~/.gemini/settings.json` |
| Codex CLI | `codex-cli` | MCP stdio | `[mcp_servers.cutlass]` in `~/.codex/config.toml` |
| Continue | `continue` | MCP stdio | `~/.continue/mcpServers/cutlass.json` (agent mode) |
| Zed | `zed` | MCP stdio | `context_servers.cutlass` in Zed's `settings.json` |
| OpenAI-compatible harness | `openai-compatible` | HTTP | `/v1/tools?format=openai` and `/v1/call`; example `build/examples/agent-openai-compatible.py` |
| Anthropic SDK harness | `anthropic-sdk` | HTTP | `/v1/tools?format=anthropic` and `/v1/call`; example `build/examples/agent-anthropic.py` |
| HTTP only (curl) | `http` | HTTP | `/v1/tools?format=markdown` or `/v1/openapi.json` and `/v1/call`; example `build/examples/agent-http.ps1` |

`<exe>` is `Cutlass.Agent.Mcp` beside `Cutlass.App` (for example `C:\Program Files\Cutlass\Cutlass.Agent.Mcp.exe` or `/opt/cutlass/Cutlass.Agent.Mcp`; during development `src\Cutlass.Agent.Mcp\bin\Debug\net10.0\Cutlass.Agent.Mcp.exe`). Every MCP config entry is that command with no arguments, for example:

```json
{ "mcpServers": { "cutlass": { "command": "C:\\Program Files\\Cutlass\\Cutlass.Agent.Mcp.exe", "args": [] } } }
```

The AGENT tab (Agent > Agent panel, or a click on the status bar badge) shows the server, the MCP exe and a "Clients" line with the number of current registrations and a Connect... button. Then ask, for example: "In Cutlass, cut the silence out of the interview and add a title 'Meet Ana' for the first 4 seconds."

**The edit lock.** An agent must take the edit lock before it changes anything. While it holds the lock, a banner names the agent and its reason and the editing UI is read-only; playback, scrubbing, Save and Export stay available. "Pause" makes the agent's next change wait, and "Take over" ends the lock at once and makes the UI editable again. The lock is a 90 s lease renewed by the agent's calls, so a stuck or vanished agent loses it on its own.

### Examples

`build/examples/` holds runnable harnesses, each documented at the top and in `build/examples/README.md` (with setup for Ollama, OpenAI, LM Studio and vLLM):

- `agent-openai-compatible.py --model <model> --task "<task>"`: any OpenAI-compatible endpoint (default Ollama at `http://localhost:11434/v1`); needs only `pip install openai`.
- `agent-anthropic.py --task "<task>"`: the same loop with the Anthropic SDK; needs only `pip install anthropic`.
- `agent-http.ps1`: no SDK, Windows PowerShell 5.1 or PowerShell 7: status, lock, describe, one edit through `/v1/call`, frame 0 to a PNG, release. CI runs it against `serve`.
- `agent-serve-demo.ps1`: starts `serve` itself, imports, places, exports H.264 and checks the file with ffprobe.

The first three find the running app or `serve` through `endpoint.json`, so they need no connection arguments.

### Headless and scripted use

MCP with the engine in process (no app, no window), for a project file; any MCP client takes the same command and arguments:

```
claude mcp add cutlass-headless -- "C:\Program Files\Cutlass\Cutlass.Agent.Mcp.exe" --headless --project "D:\Edits\ep07.cutlass" --allow-read "D:\Footage" --allow-write "D:\renders"
```

`--create` makes the project file when it is missing. On exit with unsaved changes the headless host writes an autosave that the app offers to recover; it never overwrites the project without an explicit save.

The HTTP API without MCP, for harnesses, scripts and render farms:

```
Cutlass.Agent.Mcp serve --project ep07.cutlass --port 47410 --allow-write D:\renders
```

It prints the base URL and the token file and runs until `POST /v1/shutdown` or Ctrl+C. A script then calls `POST /v1/lock/acquire`, `POST /v1/exports`, polls `GET /v1/exports/{id}?waitMs=20000` and finishes with `POST /v1/shutdown {"save": true}` (a locked call, so it still holds the lock or takes it again after `/v1/lock/release`), each request with `Authorization: Bearer <token>`. `build/examples/agent-serve-demo.ps1` does exactly this (`powershell -File build/examples/agent-serve-demo.ps1 [-McpExe <exe>] [-Port N]`).

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
- `docs/AGENT_INTEGRATION.md`: the agent API, the edit lock, the MCP server, client registration and provider neutrality (section 9).
- `build/examples/README.md`: the reference harnesses (OpenAI-compatible, Anthropic SDK, HTTP only) and how to run them.
- `docs/STATUS.md`: milestone status, test counts, smoke coverage and known limitations.
- `CLAUDE.md`: the short working guide (commands, invariants and conventions).
