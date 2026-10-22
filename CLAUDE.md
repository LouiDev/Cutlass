# Cutlass

Cutlass is a cross-platform (Windows and Linux) non-linear video editor.
- **Stack:** .NET 10 with Avalonia 11.3.22 (never 12), CommunityToolkit.Mvvm 8.4.2, and Silk.NET OpenAL for preview audio.
- **Media:** all media work runs through external `ffmpeg`/`ffprobe` processes. No native ffmpeg bindings.

## Normative documents
- `docs/DESIGN.md` covers product scope, UX, timeline behaviour, shortcuts, visual style and export presets.
- `docs/TECH_BRIEF.md` covers architecture, the file map, the compiler rules, exact ffmpeg commands, threading, testing and the build order.
- Both are normative. If you fill a gap or have to deviate, leave a short code comment and update the doc in the same change.
- Work milestone by milestone (TECH_BRIEF section 12). A milestone is done only when its Verify step passes.
- `docs/STATUS.md` records the state after the v1 close-out (2026-10-22): milestones, test counts, smoke coverage, what was verified only by reading, and the known limitations. Read it first when resuming. `docs/HANDOFF.md` only points there now.

## Commands
- Build: `dotnet build Cutlass.slnx --disable-build-servers -m:1`
- Unit tests: `dotnet test tests/Cutlass.Tests --disable-build-servers -m:1`
- Integration tests (need ffmpeg): `dotnet test tests/Cutlass.IntegrationTests --disable-build-servers -m:1`
- Refresh compiler goldens: set `CUTLASS_UPDATE_GOLDEN=1`, run the unit tests, then review the diff.
- Run: `dotnet run --project src/Cutlass.App`
  - `-- <project.cutlass>` opens a project. `-- --import <file>...` imports files into a new untitled project and places them end to end on V1/A1.
- Smoke test (needs ffmpeg and a desktop session; on Linux use `xvfb-run`): `pwsh build/smoke.ps1 [-Script m4|m5|m6|m7|m7b|m9] [-OutDir <folder>] [-AppPath <published exe>]` (Windows PowerShell 5.1 works too: `powershell -File build/smoke.ps1 ...`). `m4` (default) covers import, cut and export; `m5` covers media jobs, proxies, playback drift, live mute and solo, and 2x/4x shuttle; `m6` covers Source mode, three-point edits, match frame, copy and paste, Q/W, transitions, speed and export, then Source and Effects drops and the DESIGN 5 checklist screenshots; `m7` covers the Settings dialog and one export per DESIGN 9 preset, each checked with ffprobe; `m7b` takes a media file offline (hatched clips with the `OFFLINE` badge) and screenshots the real Relink, Settings, Export, Speed and Sequence Settings dialog windows (`show-dialog`). `m9` starts the app with `--agent-server --port 47399` and drives the agent HTTP API in process: lock, banner, read-only UI, an agent batch, silence removal, a frame PNG, pause, take over, one-step undo, an agent export and the Agent panel. A run restores the settings it started with. Smoke commands are documented in TECH_BRIEF 10.4.
  - It generates two lavfi clips, then runs `build/smoke/m4.smoke` in the real app: import, insert, scrub with frame presentation, split, ripple delete, undo, save and reopen, export through the export dialog, an ffprobe check of the file, and a timeline file drop. Exit code 0 means every step passed.
  - Underneath it is the developer mode `dotnet run --project src/Cutlass.App -- --smoke-script <file> [--smoke-log <file>]`. It answers every dialog itself, logs each step to stdout and to `<script>.log` (or `--smoke-log`), stops at the first failure and exits with 0 or 1.
  - Script commands are listed in `src/Cutlass.App/Services/SmokeScript.cs`. They include `import`, `insert`, `playhead`, `frame`, `split`, `select-at`, `ripple-delete`, `undo`, `save`, `open`, `export`, `probe`, `screenshot` and `show-dialog` (a dialog in its real window). Screenshots render the window itself, so they work while other windows cover it.
- Publish (self-contained, one folder, checked for the OpenAL native and the Inter fonts): `powershell -File build/publish.ps1 [-Runtime win-x64|linux-x64]` or `bash build/publish.sh [linux-x64|win-x64]`, into `artifacts/publish/<rid>`. It also publishes `Cutlass.Agent.Mcp` into the same folder (failing on any differing shared file), copies `docs/AGENT_INTEGRATION.md` beside the exes and runs `Cutlass.Agent.Mcp --print-registration`.
  - Always run the shell script through `bash`: Windows checkouts (`core.filemode=false`) drop its execute bit. When it is first committed, set the bit in the index with `git update-index --chmod=+x build/publish.sh`.
- MCP server: `Cutlass.Agent.Mcp` proxies to the running app; `--headless --project <file> [--create]` hosts the engine in process; `serve --project <file> [--port N]` runs the HTTP API without MCP; `--print-registration` prints the `claude mcp add` command and the Claude Desktop JSON. `--headless` and `serve` refuse a project that a live app names in `endpoint.json`.
- Serve demo (story 3, needs ffmpeg and a published or built MCP exe): `powershell -File build/examples/agent-serve-demo.ps1 [-McpExe <exe>] [-Port N]`.

## Architecture
- `src/Cutlass.Core`: BCL only. Time math (frames, flicks, samples), immutable model records, pure `TimelineEdits`, `TimelineValidator`, `SnapEngine`, `ProjectSession` (undo with gestures and coalescing), and JSON via DTOs.
- `src/Cutlass.Compiler`: pure and deterministic. Turns a timeline range into inputs plus a filtergraph, and holds the export presets and argument builders. No I/O, no processes.
- `src/Cutlass.Media`: ffmpeg discovery and capabilities, `ProcessRunner`, probe, frame index, cache, jobs, thumbnails, waveforms, proxies, `FrameServer`, `PlaybackSession`, OpenAL output, `ExportService`.
- `src/Cutlass.App`: the Avalonia WinExe. Views, view models, the custom-drawn `TimelineControl`, `FramePresenter`, and a hand-written composition root.
- `src/Cutlass.Agent` (milestones A1 to A4, built; spec in `docs/AGENT_INTEGRATION.md`): references Core, Compiler and Media, never Avalonia. It holds:
  - the agent API model and the leased edit lock;
  - the tools over `ProjectSession` and the Media services;
  - the loopback HTTP host (`HttpListener`, bearer token); responses are written through `AgentJsonContext.Relaxed` (the relaxed encoder), never the generated `Default`;
  - `IAgentHost`, which the App and the headless host implement.
- `src/Cutlass.Agent.Mcp` (A4): a console exe. It serves MCP over stdio (ModelContextProtocol 2.3.0), either proxying to the running app's HTTP API or hosting the engine itself with `--headless`. `serve` runs the HTTP API headless for scripts.
- `tests/Cutlass.Tests` needs no ffmpeg. `tests/Cutlass.IntegrationTests` uses real ffmpeg and skips itself when ffmpeg is missing.

## Invariants (do not break)
- **Time:**
  - Timeline positions are integer frames.
  - Source time is in flicks, measured from `format.start_time`.
  - Audio is 48 kHz stereo, and sample positions always come from `SamplesAt(absoluteFrame)`.
- **Model:**
  - The model is immutable.
  - Edits are pure, clamp instead of throwing, and pass `TimelineValidator` in DEBUG builds and tests.
  - A link is only a group id. Linked clips may differ in start and duration.
- **Compiler:**
  - One compiler path serves export, scrub and playback, and its output is byte-identical for the same input.
  - Numbers are formatted only through `FfNum`, with invariant culture.
- **Video segments:** `fps=R:start_time=0:round=up,tpad=stop_mode=clone:stop=N,trim=end_frame=N`.
  - Seeks are floored to the microsecond and never get a positive epsilon.
  - Never use `PTS-STARTPTS` on a media input stream.
  - Never add `transpose` or pass `-noautorotate`.
  - Never use `pad` to place PiP. Overlay at x:y instead.
- **Audio segments:** `aresample=48000:async=1:first_pts=0`, then `apad=whole_len=S,atrim=end_sample=S`, then placement with `adelay=<n>S:all=1`.
- **Graphs:**
  - A graph refers to files only by bare names inside `<cache>/work/`.
  - The script option is `-/filter_complex`, or `-filter_complex_script` only when the capability probe reports LegacyScript (ffmpeg 6.x).
  - Use only filters that exist in ffmpeg 6.0.
- **Exports:**
  - Never pass `-nostdin` (`q` on stdin is the graceful cancel).
  - Write `name.partial.ext` and rename it on success.
  - Tag the output BT.709.
- **Threading:**
  - `WriteableBitmap` is locked and copied only on the UI thread, row by row using `RowBytes`.
  - Media and Compiler never reference Avalonia.

## Conventions
- **No em dashes** (U+2014) anywhere: code, comments, docs, commit messages. `build/check-text.ps1` enforces this.
- **C# style:** file-scoped namespaces, nullable enabled, one top-level type per file (partial classes may span `Type.Part.cs` files), and namespaces that follow folders.
- **MVVM** uses the CommunityToolkit source generators:
  - Properties: `[ObservableProperty] public partial T Name { get; set; }`.
  - Commands: `[RelayCommand]`.
  - Every view and DataTemplate sets `x:DataType`, because compiled bindings are the default.
  - View models never touch controls.
- **Processes:**
  - Every ffmpeg or ffprobe call goes through `ProcessRunner`: `ArgumentList`, `CreateNoWindow`, stderr drained, Job Object or pid registry. Never start a `Process` anywhere else.
  - Never block the UI thread on a process or on file I/O. Await it and post results with `Dispatcher.UIThread.Post`.
- **Packages:** versions live only in `Directory.Packages.props`, pinned exactly. `TreatWarningsAsErrors` applies only to Release builds, so keep Debug free of warnings as well.
- **Tests:**
  - xunit 2.9.3.
  - Integration tests use `[FfmpegFact]`.
  - Number-formatting tests run under the `de-DE` culture.
  - Generate test media with lavfi sources (`testsrc2`, `sine`, `aevalsrc`, the `geq` bit-block oracle).
- **Git:** commit or push only when the user asks.
