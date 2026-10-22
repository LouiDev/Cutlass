# Cutlass status (2026-10-22)

This file records where Cutlass stands after the v1 close-out pass. `docs/DESIGN.md` and `docs/TECH_BRIEF.md` stay normative; this is a snapshot, not a spec.

## Milestones

The build order and the Verify step of each milestone are in TECH_BRIEF section 12.

| Milestone | State | Notes |
|---|---|---|
| M0 Skeleton | Done | Solution, props, six projects, dark theme, main grid and status bar. Launch verified on Windows only. |
| M1 Core | Done | Time math, immutable model, edits, validator, snapping, session and undo, JSON. Unit, fuzz and de-DE suites green. |
| M2 ffmpeg foundation | Done | Paths, settings, locator, capability probe, ProcessRunner with Job Object and pid registry, work directory, fonts, probe. |
| M3 Compiler and headless export | Done | Compiler, presets, args builder, ExportService, progress parser. Golden, determinism and exact-count integration tests green. |
| M4 Vertical slice | Done | Import, timeline, scrub, viewer, export dialog, jobs popover, save and open, welcome overlay. `m4` smoke green. |
| M5 Cache and playback | Done | Cache, scheduler, frame index, thumbnails, waveforms, proxies, OpenAL playback, mixer and master meter, 1x/2x/4x shuttle. `m5` smoke green. |
| M6 Editing depth | Done | Ripple, Roll, Slip, Q/W, transitions, titles, inspector, keyframes, markers, Source mode, three-point edits, match frame, copy and paste, speed dialog. `m6` smoke and checklist screenshots green. |
| M7 Hardening | Partial | Autosave and recovery, relink (with remote-path confirmation), settings, recent projects, error dialogs, eviction, every preset, publish scripts: all done and verified locally (`m7`, `m7b`, published `m4`). Not verified: the Linux run and CI green on both OSes, which its Verify step also asks for. |
| M8 Late items | Not started | Reverse shuttle, per-track meters, hardware encoders, segmented export. |

### Agent integration (`docs/AGENT_INTEGRATION.md` section 8)

| Milestone | State | Notes |
|---|---|---|
| A1 Library and lock | Done | `Cutlass.Agent`: API model, `ToolCatalog`, leased `AgentLock`, edit, batch and project-read tools, `PngEncoder`, `PathPolicy`, `LevelAnalyzer`, `SilenceFinder`; Core groups, write gate and `RemoveRanges`. Verify step confirmed by the integration pass (0-warning builds, unit suite with fuzz, text check). |
| A2 HTTP host and headless host | Done | `StillRenderer`, `RangeAudioReader`, the loopback `AgentHttpServer` with bearer token, `AgentClient`, `HeadlessHost`. `HttpHostTests`, `HttpSecurityTests` and the 7.2 tests green; a plain-user session against `http://localhost:<port>/` (the serve demo) settles the URL ACL risk. JSON responses use the relaxed encoder, like the project file. |
| A3 App integration and UI | Done | `AppAgentHost`, command gating, read-only timeline and inspector, banner, Agent panel, badge, Settings > Agents, `--agent-server`, smoke `m9`. The grant closes the marker editor and rename box; the welcome overlay no longer covers the banner; long folder paths trim with a tooltip. `m9` and m4 to m7b green; the banner, panel and Settings > Agents screenshots were reviewed against DESIGN 8. |
| A4 MCP exe and docs | Done | `Cutlass.Agent.Mcp`: proxy (`HttpBackend`, `EndpointLocator`), `--headless` (`InProcessBackend`), `serve`, `--print-registration`; every catalog MCP tool (dispatch serialised in arrival order), the `cutlass://` resources and the `cutlass_workflow` prompt. `--headless` and `serve` refuse a project a live app has open (`McpProjectOwnerTests`). `McpEndToEndTests` green; both publish scripts place the MCP exe beside the app (win-x64 and linux-x64) and copy the agent docs. Story 3 runs as `build/examples/agent-serve-demo.ps1` against the published exe (H.264 export, 120 frames by ffprobe, the server exits 0). Story 1 was run by hand on 2026-10-22: `claude mcp add cutlass -- <published exe>` reported Connected, a `claude -p` session acquired the lock as "Claude Code", added a centred title on V2 (frames 0 to 90), split V1 at frame 120 inside one batch, rendered frame 30 as an image and released the lock after 10 lock checks; the app showed the banner, the read-only inspector and the agent badge throughout, and the batch landed as the single undo step "Agent: Story 1 demo". A nested Claude Code session in plan mode makes no tool calls, so pass `--permission-mode default` (or approve the plan) for unattended runs. |

## Tests

| Suite | Count | Result on this machine |
|---|---|---|
| `tests/Cutlass.Tests` (no ffmpeg) | 1744 | All green (Debug) |
| `tests/Cutlass.IntegrationTests` (real ffmpeg, `[FfmpegFact]`) | 247 | All green, run three times in a row; no ffmpeg, ffprobe, Cutlass.App or Cutlass.Agent.Mcp process and no `endpoint.json` left afterwards |

The provider-neutrality pass adds `tests/Cutlass.IntegrationTests/Examples` (6 tests, green: `agent-http.ps1` against an in-process headless host on the split and the marker path, the Python harnesses parse, stay under 150 lines and pass `check_harness.py` for `openai` and `anthropic`); the PowerShell tests skip without PowerShell and the Python ones without Python. The counts in the table include this pass (refreshed by the integration run of 2026-10-22, Debug and Release builds with 0 warnings). `VendorNeutralityTests` scans `src/Cutlass.App` (no vendor or client name outside `Assets/agent-clients.json`) and also `src/Cutlass.Agent` and `src/Cutlass.Agent.Mcp` (only the tool format ids in the exporters and the `discovery.tools` entry); `AgentClientRegistryTests` checks that the README client matrix lists every profile id in order.

Debug and Release builds with `--no-incremental` finish with 0 warnings and 0 errors (Release treats warnings as errors). `build/check-text.ps1` is clean.

GitHub Actions (first runs on 2026-10-22): the Ubuntu leg is green end to end, including the real-time playback tests and the M4 smoke under Xvfb, so the Linux build has now run on real hardware. The Windows leg skips the three `[RealtimeFact]` playback tests (`CUTLASS_SKIP_REALTIME=1`) because the shared runners stall for up to a second at a time; everything else runs there too.

## Agent menu and briefing (2026-10-22)

Built from `docs/MCP_HOSTING_CONCEPT.md` (accepted): an Agent menu (Start / Stop MCP server on F9, Copy briefing for your agent, the HTTP-only variant, Register with Claude Code, Show registration for other clients, Agent panel, Take over), the MCP badge states in the status bar, the MCP SERVER header block in the Agent panel, a first-start dialog that can register the exe with Claude Code, and the briefing renderer with live project values (`AgentBriefing`, templates embedded, user override at `<data>/agent/briefing.md`). Verified by hand: the rendered briefing pasted into a `claude -p` session as the only context made the agent acquire the lock, add the requested title inside one batch, render and check frame 30 and release the lock; a second session started at the same time waited for the lock and did not duplicate the edit. Smoke `m9` covered the menu, the badge, the dialog and both briefings (69 steps at the time). The Claude Code specific items were later replaced by the client profiles (next section).

## Provider neutrality (2026-10-22, `docs/AGENT_INTEGRATION.md` section 9)

Cutlass no longer treats one vendor as special: the tool catalog is served in every harness format, every tool is callable through one route, and Claude is one row of the client table.

Discovery endpoints (all `GET`, bearer token, no lock):

| Route | Returns |
|---|---|
| `/v1/tools` | The catalog as is: name, `mcpName`, description, `needsLock`, `mutating`, `heavy`, result kind, JSON schema. |
| `/v1/tools?format=openai` | `[{type: "function", function: {name, description, parameters}}]` for OpenAI-compatible endpoints (Ollama, LM Studio, vLLM, OpenAI, ...). |
| `/v1/tools?format=anthropic` | `[{name, description, input_schema}]`. |
| `/v1/tools?format=gemini` | `[{functionDeclarations: [...]}]` with the schema reduced to what Gemini accepts. |
| `/v1/tools?format=markdown` | A readable reference, one section per tool. |
| `/v1/openapi.json` | OpenAPI 3.1 for every route, with the envelopes and bearer security. |
| `/v1/instructions` | `{text}`: the workflow rules of 5.4 plus the `/v1/call` contract. |

`POST /v1/call` takes the catalog name (`edit.split`) or the MCP name (`cutlass_split`), and image tools called through it answer with `pngBase64` in the envelope.

Reference harnesses in `build/examples/` (documented in `build/examples/README.md`):

| File | State |
|---|---|
| `agent-openai-compatible.py` | Written (149 lines, `openai` SDK only, `urllib` for Cutlass). Parses; `check_harness.py` loads it with a stub SDK and a fake transport serving the real `format=openai` document and checks the tool loading, the envelope handling, the lock header and the PNG saving. **A model-driven run is pending**: it was put off on the user's request. |
| `agent-anthropic.py` | Written (149 lines, `anthropic` SDK only). Same offline checks with `format=anthropic`. No model call was made (no API key used). |
| `agent-http.ps1` | Green: `HttpExampleTests` runs it in Windows PowerShell 5.1 against an in-process headless host over a placed clip (split path) and an empty project (marker path); run by hand against `serve` with the CI step's commands, it found `serve` through `endpoint.json` and passed. CI runs it on Windows against `serve` (never executed on GitHub yet). |

Findings from a first attempt with gpt-oss 20B on Ollama, stopped when the run was put off (not a result): Ollama gives many models a 4096-token context by default, and the first request (57 tool definitions plus the instructions) is about 5300 tokens, so Ollama has to be started with a larger `OLLAMA_CONTEXT_LENGTH` (the examples README says so). The model asked for a 15 s lease and lost it while thinking; the harnesses now renew the lease every 10 s while the model thinks.

## Smoke scripts

Run with `powershell -File build/smoke.ps1 -Script <name>` (or `pwsh`). Each generates its own lavfi clips, drives the real app through `--smoke-script`, logs every step and exits 0 only when every step passed. All six are green on this machine (2026-10-22: m4 55 steps, m5 56, m6 134, m7 59, m7b 31, m9 92).

| Script | Covers |
|---|---|
| `m4` (default) | Import, insert, scrub with frame presentation, split, ripple delete, undo, save and reopen, export through the export dialog, an ffprobe check of the file, and a timeline file drop. Also run against the published win-x64 exe (`-AppPath ... -NoBuild`). |
| `m5` | Media jobs, proxies, playback drift on the audio device, the master meter, live mute and solo, and 2x/4x shuttle. |
| `m6` | Source mode, three-point edits, match frame, copy and paste, Q/W, transitions, speed and export, Source and Effects drops, and the DESIGN 5 checklist screenshots. |
| `m7` | The Settings dialog and one export per DESIGN 9 preset, each checked with ffprobe. |
| `m7b` | A media file taken offline (hatched clips with the `OFFLINE` badge, striped bin card) and screenshots of the real Relink, Settings, Export, Speed and Sequence Settings dialog windows. |
| `m9` | The agent HTTP API in process (`--agent-server --port 47399`): lock, banner, read-only UI, an agent batch, silence removal, a frame PNG, pause, take over, one-step undo, an agent export, the Agent panel and Settings > Agents; the Agent menu (briefings, Stop and Start, the open menu and its "Copy briefing for" submenu); the agent clients in the sandbox home (detect, register, update, remove, the briefing per target, Connect clients screenshots). 92 steps. Also run against the published win-x64 exe. |

## Verified on this machine versus only by reading

Verified by running on Windows 11 (x64):

- Debug and Release builds, the unit and integration suites, all six smoke scripts, the text check.
- `build/publish.ps1` for win-x64 (both exes, `AGENT_INTEGRATION.md` and `agent-clients.json`), the m4 and m9 smokes on the published app, `Cutlass.Agent.Mcp --print-registration` (one block per profile, 12, checked by the publish script), and `build/publish.ps1 -Runtime linux-x64` (cross-publish; the Linux exes never ran).
- `build/examples/agent-serve-demo.ps1` (story 3) against the published `Cutlass.Agent.Mcp serve`.
- `dotnet publish src/Cutlass.App -c Release -r linux-x64 --self-contained` builds and the folder holds `Cutlass.App`, `libopenal.so` and the Inter fonts.
- The m6, m7b and m9 screenshots were reviewed for visual defects.

Verified only by reading the code (never executed):

- Everything on Linux: the app itself, X11 and XWayland, OpenAL Soft from the bundled `libopenal.so`, fontconfig lookup, XDG paths, POSIX signal autosave, the `/proc` based process and mount checks, the execute-bit restore for a picked ffmpeg, `build/publish.sh`, and `xvfb-run` smoke runs.
- CI (`.github/workflows/ci.yml`): the workflow has never run on GitHub.
- The relink "Confirm location" flow against a real network share (covered by unit tests with injected lookups only).

## Known limitations

- Linux has never been run on real hardware; every Linux path was verified by reading only. CI has never executed on GitHub.
- Avalonia 11.3.22 on X11 has no XDND, so OS file drops do nothing on Linux; the UI points at Ctrl+I and the Import button there.
- Two instances sharing one cache can evict each other's proxies; the app detaches and re-queues lost proxies but does not lease artifacts.
- atempo alignment is calibrated with tone bursts (mean within 1 ms); a single click cannot be held to 1 ms by any constant compensation.
- M8 items are not built: reverse shuttle (J steps back one frame), per-track meters, hardware encoders, segmented export for very large timelines.

## How to try it

Prerequisites:

- .NET 10 SDK.
- ffmpeg 6.0 or newer with ffprobe, on `PATH` or chosen in Settings (Windows: `winget install Gyan.FFmpeg`; Debian and Ubuntu: `sudo apt install ffmpeg`).
- A desktop session (on Linux, X11 or XWayland; `xvfb-run` for headless smoke runs).

Build and test:

```
dotnet build Cutlass.slnx --disable-build-servers -m:1
dotnet test tests/Cutlass.Tests --disable-build-servers -m:1
dotnet test tests/Cutlass.IntegrationTests --disable-build-servers -m:1
```

Run:

```
dotnet run --project src/Cutlass.App
dotnet run --project src/Cutlass.App -- --import clip1.mp4 clip2.mp4
dotnet run --project src/Cutlass.App -- my-edit.cutlass
```

`--import` places the files end to end on V1/A1 in a new untitled project.

Smoke and publish:

```
powershell -File build/smoke.ps1 -Script m4
powershell -File build/publish.ps1 -Runtime win-x64
powershell -File build/smoke.ps1 -AppPath artifacts/publish/win-x64/Cutlass.App.exe -NoBuild
powershell -File build/examples/agent-serve-demo.ps1
```

Agent examples (`build/examples/README.md`; start the app with agent connections on, or `serve`, first):

```
powershell -File build/examples/agent-http.ps1
python build/examples/agent-openai-compatible.py --model gpt-oss:20b --task "<task>"
python build/examples/agent-anthropic.py --task "<task>"
```

Nothing has been committed yet: `git status` shows the whole tree as untracked.

---

**TLDR:** M0 to M6 are done, M7 is done locally but its Linux run and CI parts were never executed, M8 is not started. The agent milestones A1 to A4 are done, including story 1 by hand with Claude Code against the published exe. 1744 unit and 247 integration tests, six smoke scripts (m9 included), the win-x64 publish with both exes and the serve demo are green on Windows; everything Linux and CI is verified by reading only. Provider neutrality (section 9): the discovery endpoints, the client profiles and three reference harnesses exist; the HTTP example is green against a headless host and `serve`, while the model-driven harness run is pending (user's request); the Python harnesses pass `py_compile` and the offline `check_harness.py` against the real served tool documents.
