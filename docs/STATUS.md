# Cutlass status (2026-11-26)

This file records where Cutlass stands after the v1 close-out pass (2026-10-22), the five effects batches (2026-10-24 and 2026-10-25) and the merge of the folder split with them (2026-11-26). `docs/DESIGN.md` and `docs/TECH_BRIEF.md` stay normative; this is a snapshot, not a spec. Dated entries below say what was run on that day; the Repository section says what was run on the merged tree.

## Repository (2026-11-26)

- `main` is in sync with `origin/main` (`https://github.com/LouiDev/Cutlass`) and is the only branch, locally and on GitHub; there are no tags and no pull requests.
- The merge commit `d037c4c` joins the folder split (`e1e126f`, 2026-10-23, TECH_BRIEF 2 "Subfolders") with the effects batches 1 to 5, which were written on the old layout. The files the batches added moved into the split folders with matching namespaces: `Api/Dto/Project/` (`VideoEffectsDto`, `AudioEffectsDto`, `GeneratorDto`), `Api/Requests/Clips/FreezeRequest`, the new `Api/Requests/Generators/` (`GeneratorAddRequest`, `GeneratorUpdateRequest`) and `Tools/Edit/EditTools.Generators` in Cutlass.Agent, and `ViewModels/Inspector/` (`InspectorPropsSectionBase`, `GeneratorSectionViewModel`) in Cutlass.App. The merged branch `effects-batches` was deleted afterwards.
- Run on the merged tree on this machine (Windows 11, x64): the Release build (0 warnings, 0 errors), the unit suite (1894 green), the integration suite (279 green, none skipped) and `build/check-text.ps1`. Not rerun on the merged tree: the Debug build, the six smoke scripts and the publish scripts.
- Run on the merged tree by GitHub Actions (`d037c4c`, both legs green): the Release build, both test suites and the text check on Windows and Ubuntu, the M4 smoke on both (Ubuntu under Xvfb), the M7 smoke and `agent-http.ps1` against `serve` on Windows.
- The folders under `artifacts/publish/` (not tracked) date from 2026-10-22, before the effects batches and the folder split. Publish again before using or registering those exes: they serve `apiRevision` 2 without the freeze and generator tools.

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
| M7 Hardening | Done | Autosave and recovery, relink (with remote-path confirmation), settings, recent projects, error dialogs, eviction, every preset, publish scripts: all done and verified locally (`m7`, `m7b`, published `m4`). Its Verify step also asks for CI green on both OSes, which holds since 2026-10-22 and again on the merge commit of 2026-11-26. The Linux run so far is the CI runner's (X11 under Xvfb); a desktop session and XWayland have not been tried. |
| M8 Late items | Not started | Reverse shuttle, per-track meters, hardware encoders, segmented export. |

### Effects batch 1: transition kinds (2026-10-24)

`TransitionKind` grew from 8 to 41 video kinds (plus Audio Crossfade), every one an ffmpeg 6.0 `xfade` transition mapped explicitly in `VideoEmitter.XfadeName` and checked against the literal 6.0 list in `TimelineCompilerTests.XfadeNamesCoverEveryVideoKind`. The Effects tab lists them under FADES, WIPES, SLIDES, SHAPES and AUDIO (`EffectItem.TransitionGroup`), the agent API lists them from the enum (`apiRevision` 3), and projects persist them by camelCase name without a schema bump. New coverage: the `transition_kinds_more` compiler golden, the `TransitionTests` theory (kept on V1, coerced on A1), serializer round trips, the Effects tab name, badge and group test, the fuzz test picks from the whole enum, and `TransitionKindsTests` renders every kind on real ffmpeg with window equality. `m6` adds `add-transition wipeup 20` before the export and a `transition:Pixelize` drop after it (138 steps, green, screenshots reviewed).

### Effects batch 2: static video effects (2026-10-24)

`VideoProps` gained a sixth parameter, `VideoEffects Effects` (flip H/V, rotation, hue, colour temperature, tint, blur, sharpen, vignette, grayscale and a chroma key), clamped in `SanitizeVideo`, persisted as `video.effects` without a schema bump (old files read `VideoEffects.None`), and emitted by `VideoEffectFilters` at the TECH_BRIEF 7.3 positions (picture filters between `scale` and `setsar`, `chromakey` and `rotate` after `format=yuva420p`); a neutral clip emits nothing, so every earlier compiler golden and the 7.8 script are unchanged. `FfmpegCapabilities.RequiredFilters` grew by the ten filters. The Inspector shows EFFECTS and CHROMA KEY groups (toggle rows through `VideoToggle`, the colour row shares `InspectorColorField`), the Effects tab has Grayscale, Flip Horizontal, Flip Vertical and Reset Effects presets, the `fx` badge compares the whole `VideoProps` except the fades, and the agent API takes an `effects` object in `clip-props` and reports it in the summary (`apiRevision` 4, `describe_project` tokens). New coverage: the `video_effects` and `video_effects_in_run` compiler goldens with exact-order facts, serializer and fuzz coverage, the Inspector and Effects tab tests, `WindowEqualityTests` with every effect on the PiP clip, and `VideoEffectsTests` on real ffmpeg (oracle flips and a half turn, chroma key over the red still with the new `green.png` recipe, BT.709 tags after a temperature change, blur and vignette decoding). `m6` adds `clip-prop V1 150 rotation=15 hue=40` and an `adjust:Grayscale` drop before the export (146 steps, green, screenshots reviewed). Known limitation: the chroma key decides per 4:2:0 chroma sample, so its matte is 2x2 blocky at the edges.

### Effects batch 3: per-clip audio effects (2026-10-24)

`AudioProps` gained a fifth parameter, `AudioEffects Effects` (high-pass, low-pass, bass and treble shelves, compressor with threshold and ratio, gate with threshold, constant-power pan and a mono sum that stays stereo), clamped in `SanitizeAudio` (cut-offs below their lowest audible value snap to 0, which is off), persisted as `audio.effects` without a schema bump (old files read `AudioEffects.None`), and emitted by `AudioEffectFilters` as TECH_BRIEF 7.3 step 3a, between `aformat` and `apad` where the stream is 48 kHz stereo fltp, every filter 1:1 in samples, before the asetpts offset, the envelope and afade; a neutral clip emits nothing, so every earlier compiler golden is unchanged. `FfmpegCapabilities.RequiredFilters` grew by highpass, lowpass, bass, treble, agate, acompressor and pan. The Inspector shows an AUDIO EFFECTS group after AUDIO (the keyframe button stays the last AUDIO row), the Effects tab has Mono, Voice (high-pass 80 Hz) and Reset Audio Effects presets, audio clips with an effect show the `fx` badge, and the agent API takes an `audioEffects` object in `clip-props` and reports `effects` in every audio clip's `audio` block (`apiRevision` 5, `describe_project` tokens `hp`, `lp`, `bass`, `treble`, `comp`, `gate`, `pan`, `mono`). New coverage: the `audio_effects` compiler golden (all effects, a high-pass after the atempo block at speed 50, treble and pan on a keyed, faded clip in an offset window) with exact-position facts, `AudioEffectFiltersTests` under de-DE, Core clamp, fuzz and serializer goldens, agent goldens, Inspector and Effects tab tests, and on real ffmpeg `AudioEffectsTests` (sine-oracle RMS deltas per filter, the pan law, the mono downmix of the new `stereo_tones.wav` recipe, exact sample counts), effects on the envelope clip of `FeatureCoverageTests` and a latency check in `AtempoAlignmentTests`. `m6` adds `clip-prop A1 150 high-pass=120 pan=-30` and an `adjust:Mono` drop before the export (151 steps, green, screenshots reviewed). Deviation from the batch spec: the treble oracle asserts a drop between 0.1 and 2 dB at 1 kHz (measured 0.19 dB, the RBJ high shelf at Q 0.707; the spec's 3 dB is not what a 3 kHz shelf does 1.6 octaves below) and the compressor oracle a gain reduction between the RMS and the peak law, 4.2 to 7 dB (measured 6.0 dB; ffmpeg's "rms" detector with the 5 ms attack rides near the peaks of a steady sine). Known limitation: the filter state starts cold at every playback window seam (see below).

### Effects batch 4: title templates in Core and freeze frames (2026-10-25)

The three Effects tab title presets moved from the App into `Cutlass.Core.Model.TitleTemplates` and gained Subtitle (Inter Regular 44 on a `#000000A0` box, lower third), Headline (Inter Bold 120, 4 px outline, top) and Quote (Inter SemiBold 72 on a `#00000080` box, centred); `TitleTemplates.Scale` holds the `height / 1080` scaling the App used to do, so the Effects tab and the agent's `add_title` (`template`, listed in `capabilities.titleTemplates`) place identical titles. `MediaClip` gained the init-only `Freeze` flag: a frozen clip shows the source frame at `SourceInFlicks` for its whole length (`TimeMath.SourceFlicksAt` returns it for every frame, which covers the compiler seek, the filmstrip, the viewer placeholder, match frame and the agent's `sourceOutFlicks`), is unbounded like a still (`TimelineQuery.IsBounded`), keeps its frame when trimmed, moved or rate-converted (`Work.IsUnbounded` for the handle, speed, slip and trim-head sites while `IsStill` keeps forcing a still's source to 0), refuses speed and slip, and persists as the clip-level `freeze: true` written only when set. `TimelineEdits.FreezeFrame` splits the clip at the frame and inserts (or overwrites with) a hold with the speed-aware source position, the clip's look without fades and no link. The compiler emits `fps,trim=end_frame=1,tpad,trim` for the frozen chain with a margin-sized input and the speed marker 0, so a frozen and a moving use of one source never share an input; a frozen still stays a still. The App has Shift+F and Clip > Freeze Frame at Playhead (`TimelineViewModel.FreezeFrameCommand`, the selected video clip under the playhead or the clip on the first targeted video track, the default still duration, ripple on), the Freeze frame toggle in the Inspector CLIP group (hides the Speed row like a still), the `hold` badge and the Freeze Frame preset (drop: the frame under the pointer); the agent API has `edit.freeze` / `cutlass_freeze_frame`, `clip-props.freeze`, the summary flag and the `freeze` describe token (`apiRevision` 6). New coverage: `TitleTemplatesTests`, `FreezeFrameTests` (ripple and overwrite, the clip start, speed, the look, refusals, session undo), `PropsTests` and `TimelineValidatorTests` for frozen clips, fuzz placements and freezes, the serializer golden with a frozen clip, the `freeze_frame` compiler golden with its chain and input facts, agent goldens and the `EditToolsTests` fact, the Effects tab, Inspector, timeline command and `TimelineBadgesTests` (the badge texts now come from the pure `TimelineControl.Badges`), and on real ffmpeg the frozen oracle frames in a full render and a mid-clip window (`OracleAccuracyTests`), a frozen clip in `WindowEqualityTests` and the three template renders in `TitleRenderTests`. `m6` adds `freeze-frame V1 200 30` before the export (the probe checks 470 frames) and `shots/15-freeze-frame.png` (160 steps, green, screenshot reviewed). Deviations from the batch spec: the hold copies the clip's `VideoProps` without its fades (a fade in the middle of the source clip would otherwise appear at the hold's edges), the timeline badge reads `hold` (plain text, so it renders with the bundled Inter on every OS) and the Effects tab badge `FRZ`, and the summary writes `freeze` only when true (the spec allowed either). Review fixes (2026-10-25): `FreezeFrame` splits the whole link group in one call, so the right-hand video and audio share one link id with ripple on or off, and the hold copies the clip's enabled state; unfreezing a hold longer than its remaining source trims it (or removes it) with a warning instead of leaving an invalid project; `Freeze` is video only (`SanitizeClip` clears it on audio, the reader ignores it there); `SyncOffset` is null for frozen clips and their partners; a relink or replace to a shorter file moves a held frame past the new end to the last frame; the Inspector toggle, `clip-props` and the badge leave stills alone; the Source rows hide in place when every source clip is frozen; Shift+F with nothing to freeze says so in a toast; the speed dialog skips frozen clips; and `edit.title.add` without a template keeps the unscaled `TitleSpec.Default` (only a template is scaled to the sequence height).

### Effects batch 5: generator clips (2026-10-25)

A new clip type, `GeneratorClip(Id, Start, Duration, Enabled, Kind, Color, ToneHz, Video, Audio)` with `GeneratorKind { ColorMatte, Bars, Tone }` (`GeneratorKinds.IsVideo`, `IsAudio`, `TrackKindOf`, `DisplayName`): a colour matte (`#RRGGBB`, black by default) and SMPTE HD bars on video tracks, a sine tone (20 to 20000 Hz, 1 kHz by default, amplitude 1/8 per channel, so -18.06 dBFS peak per channel, plus `Audio.GainDb`) on audio tracks. Every clip-type switch handles it: `SanitizeClip` (both prop sets, the colour, the frequency, no link), `WithFades`, `ScaleClipTimes`, `TrimHeadTo` and `SplitAt` (fades and the tone's keyframes through the new `KeysOf`/`WithKeys`), `Place` (track kind), `UpdateClips` (a kind change stays on its side), `Link`, `SetSpeed` and `Slip` (refused with a warning), the validator (track kind, no link, fades and keys for both sides), `TimelineQuery.DisplayName`, `Work.Name`, `IsBounded` (false), `ClipClipboard` and the fuzz test. The project file has the `generator` clip type (`generator`, `color` for a matte, `toneHz` for a tone, then `video` or `audio`), no schema bump. The compiler draws the matte as `color=...,format=rgb24` and the bars with `smptehdbars`, both cropped and scaled like a media clip whose source is the canvas (`out_color_matrix=bt709:out_range=tv`), then the shared effects, alpha, opacity and fade chain; the tone is a stereo `aevalsrc` whose phase counts from timeline sample 0 (`n+SamplesAt(segment start)`), so windows, chunks and split halves join without a click, with the shared audio tail; a hand-edited out-of-range frequency is clamped with a warning. `smptehdbars` and `aevalsrc` are required filters. The App has a GENERATORS list in the Effects tab (Color Matte, Bars, Tone; the default still duration; Apply never overwrites: the matte and bars take the title track rule, the tone the first free targeted audio track, else any free one, else a new track; drops overwrite on the track they land on, with a ghost in the new olive `GeneratorClipColor` `#8A7F45`), the Generator section in the Inspector (Color or Frequency, then the shared groups of its side; the Transform to Audio Effects groups moved into `InspectorPropsSectionBase`, which the media clip section now uses too), generator fades, badges and clip colour on the timeline, and on a tone the volume line and keyframes of audio media (`Core.Editing.ClipAudio` serves the timeline, the Inspector and the agent). The agent API has `edit.generator.add` and `edit.generator.update` (`cutlass_add_generator`, `cutlass_update_generator`), `capabilities.generatorKinds`, the `generator` summary object, a `describe_project` line (with `keys N` on a keyed tone), generator arms in `edit.clip-props` and tones in `edit.volume-keys`, at `apiRevision` 7. Placing a title or a generator no longer marks a never-used sequence as used: only a media clip sets `HasHadClips`, so footage placed after bars still sets the sequence size and rate. New coverage: `GeneratorEditTests` (11), generator facts in the validator (4), serializer (2), model, fuzz, compiler (4 facts and the tone-frequency clamp theory, the `generators` and `generator_in_run` goldens; no existing compiler golden changed), planner, agent (`EditToolsTests`, JSON round trips, regenerated agent goldens), `GeneratorAppTests` (6) and five Effects tab facts; on real ffmpeg `GeneratorTests` (5: the red matte pixel, the bars thirds, the tone level of -18.06 dBFS peak and -21.07 dBFS RMS per channel with the exact sample count plus a -6 dB gain and the Tone fixture 3.01 dB below, a later tone window equal to the full render within 1 LSB, and a 441 Hz tone split at frame 23 equal to the unsplit tone within 1 LSB) and a matte run in `WindowEqualityTests`, and `HeadlessWorkflowTests` adds and recolours a matte through `AgentClient`. `m6` drops a Color Matte on V3 and a Tone on a new A3, sets them through `clip-prop` and takes `shots/16-generators.png` (175 steps, green, screenshot reviewed: the Tone's Generator section with Frequency 440 Hz and Gain -12 dB, the 50 percent orange matte over clipB in the viewer). Deviations from the batch spec: the generator chain always scales with the BT.709 output options, also when untransformed (without it a one-frame window of transformed bars over an ended V1 clip differed from the full render, and the color source alone encodes BT.601 values); `edit.clip-props` lets a matte or bars take transform and colour as well as opacity, fades and effects (the whole video side), and a tone gain and audio effects; `edit.generator.update` also takes `kind` within one track kind; the theme has one palette, so `GeneratorClipColor` is defined once.

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
| `tests/Cutlass.Tests` (no ffmpeg) | 1894 | All green: Debug on 2026-10-25 (effects batch 5 added 39 with its review fixes), Release on the merged tree on 2026-11-26 |
| `tests/Cutlass.IntegrationTests` (real ffmpeg, `[FfmpegFact]`) | 279 | All green on the merged tree (Release, 2026-11-26, 4 m 55 s, none skipped; no ffmpeg, ffprobe or Cutlass.App process and no `endpoint.json` left afterwards). Before the merge (2026-10-25, 4 m 58 s): 278 green and `OpenAlAudioOutputTests.PlaysOneSecondOfSilenceAndToneWithAMonotonicClock` skipped, because the machine had no audio device during that run. The 247 of the v1 close-out ran three times in a row; effects batch 1 added one, batch 2 added 7, batch 3 added 12, batch 4 added 6 and batch 5 added 6 with its review fixes |

The provider-neutrality pass adds `tests/Cutlass.IntegrationTests/Examples` (6 tests, green: `agent-http.ps1` against an in-process headless host on the split and the marker path, the Python harnesses parse, stay under 150 lines and pass `check_harness.py` for `openai` and `anthropic`); the PowerShell tests skip without PowerShell and the Python ones without Python. The counts in the table include this pass (refreshed by the integration run of 2026-10-22, Debug and Release builds with 0 warnings). `VendorNeutralityTests` scans `src/Cutlass.App` (no vendor or client name outside `Assets/agent-clients.json`) and also `src/Cutlass.Agent` and `src/Cutlass.Agent.Mcp` (only the tool format ids in the exporters and the `discovery.tools` entry); `AgentClientRegistryTests` checks that the README client matrix lists every profile id in order.

Debug and Release builds with `--no-incremental` finished with 0 warnings and 0 errors on 2026-10-25 (Release treats warnings as errors). On the merged tree the Release build was run, here and on GitHub, with 0 warnings and 0 errors; the Debug build was not rebuilt. `build/check-text.ps1` is clean.

GitHub Actions (`.github/workflows/ci.yml`, TECH_BRIEF 11.3): four of the first five runs on 2026-10-22 failed; the last three runs on `main` are green on both legs (`b9047d6` on 2026-10-22, `e1e126f` on 2026-10-23, `d037c4c` on 2026-11-26). The Ubuntu leg is green end to end, including the real-time playback tests and the M4 smoke under Xvfb, so the Linux build runs on a real machine, though not in a desktop session. The Windows leg skips the three `[RealtimeFact]` playback tests (`CUTLASS_SKIP_REALTIME=1`) because the shared runners stall for up to a second at a time; everything else runs there too, plus the M7 smoke and the HTTP example against `serve`. The `publish` job runs only on a `v*` tag and has never run.

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
| `agent-http.ps1` | Green: `HttpExampleTests` runs it in Windows PowerShell 5.1 against an in-process headless host over a placed clip (split path) and an empty project (marker path); run by hand against `serve` with the CI step's commands, it found `serve` through `endpoint.json` and passed. CI runs it on Windows against `serve` (green on GitHub, last on 2026-11-26). |

Findings from a first attempt with gpt-oss 20B on Ollama, stopped when the run was put off (not a result): Ollama gives many models a 4096-token context by default, and the first request (57 tool definitions at the time, 60 since effects batch 5, plus the instructions) was about 5300 tokens, so Ollama has to be started with a larger `OLLAMA_CONTEXT_LENGTH` (the examples README says so). The model asked for a 15 s lease and lost it while thinking; the harnesses now renew the lease every 10 s while the model thinks.

## Smoke scripts

Run with `powershell -File build/smoke.ps1 -Script <name>` (or `pwsh`). Each generates its own lavfi clips, drives the real app through `--smoke-script`, logs every step and exits 0 only when every step passed. None was rerun on this machine after the merge of 2026-11-26; CI ran `m4` on Windows and Ubuntu and `m7` on Windows on the merge commit, all green. All six were green on this machine before the merge (2026-10-22: m4 55 steps, m5 56, m6 134, m7 59, m7b 31, m9 92; 2026-10-24: m6 138 after effects batch 1, 146 after effects batch 2, 151 after effects batch 3; 2026-10-25: m6 160 after effects batch 4, 175 after effects batch 5).

| Script | Covers |
|---|---|
| `m4` (default) | Import, insert, scrub with frame presentation, split, ripple delete, undo, save and reopen, export through the export dialog, an ffprobe check of the file, and a timeline file drop. Also run against the published win-x64 exe (`-AppPath ... -NoBuild`). |
| `m5` | Media jobs, proxies, playback drift on the audio device, the master meter, live mute and solo, and 2x/4x shuttle. |
| `m6` | Source mode, three-point edits, match frame, copy and paste, Q/W, transitions, speed, Inspector effect rows (`clip-prop`, video and audio), Grayscale and Mono preset drops, a 1 s freeze frame (`freeze-frame`) and a Color Matte and a Tone (`effect-drop generator:...`, `clip-prop` on the Generator section), all rendered by the export, Source and Effects drops, and the DESIGN 5 checklist screenshots (plus `shots/15-freeze-frame.png` and `shots/16-generators.png`). |
| `m7` | The Settings dialog and one export per DESIGN 9 preset, each checked with ffprobe. |
| `m7b` | A media file taken offline (hatched clips with the `OFFLINE` badge, striped bin card) and screenshots of the real Relink, Settings, Export, Speed and Sequence Settings dialog windows. |
| `m9` | The agent HTTP API in process (`--agent-server --port 47399`): lock, banner, read-only UI, an agent batch, silence removal, a frame PNG, pause, take over, one-step undo, an agent export, the Agent panel and Settings > Agents; the Agent menu (briefings, Stop and Start, the open menu and its "Copy briefing for" submenu); the agent clients in the sandbox home (detect, register, update, remove, the briefing per target, Connect clients screenshots). 92 steps. Also run against the published win-x64 exe. |

## Verified on this machine versus only by reading

Verified by running on Windows 11 (x64), before the merge unless the Repository section says otherwise:

- Debug and Release builds, the unit and integration suites, all six smoke scripts, the text check.
- `build/publish.ps1` for win-x64 (both exes, `AGENT_INTEGRATION.md` and `agent-clients.json`), the m4 and m9 smokes on the published app, `Cutlass.Agent.Mcp --print-registration` (one block per profile, 12, checked by the publish script), and `build/publish.ps1 -Runtime linux-x64` (cross-publish; the Linux exes never ran).
- `build/examples/agent-serve-demo.ps1` (story 3) against the published `Cutlass.Agent.Mcp serve`.
- `dotnet publish src/Cutlass.App -c Release -r linux-x64 --self-contained` builds and the folder holds `Cutlass.App`, `libopenal.so` and the Inter fonts.
- The m6, m7b and m9 screenshots were reviewed for visual defects (effects batch 4: `shots/15-freeze-frame.png` shows the selected hold with the `hold` badge, the Freeze frame toggle on, no Speed row, the held frame in the viewer and the audio gap under the hold).
- The timeline badges: since effects batch 4 their texts and order (`OFFLINE`, speed, `hold`, `fx`, sync) come from the pure `TimelineControl.Badges`, which `TimelineBadgesTests` runs, and their drawing is in the reviewed m6 screenshots.

Verified by GitHub Actions on Ubuntu 24.04 (ffmpeg 6.1), last on 2026-11-26:

- The Release build, the unit and integration suites with the real-time playback tests, the text check, and the M4 smoke in the real app on X11 under `xvfb-run`. The fontconfig lookup, the XDG paths and the `/proc` based checks are reached there as far as those suites cover them.

Verified only by reading the code (never executed):

- On Linux, everything the CI runner does not reach: a desktop session with a window manager, XWayland, audible output through OpenAL Soft from the bundled `libopenal.so`, the smoke scripts other than `m4`, POSIX signal autosave, the execute-bit restore for a picked ffmpeg, `build/publish.sh` and the published linux-x64 exes.
- The `publish` job of CI: it runs only on a `v*` tag, and no tag has been pushed.
- The relink "Confirm location" flow against a real network share (covered by unit tests with injected lookups only).

## Known limitations

- Linux has only run on the CI runner (the test suites and the M4 smoke under Xvfb), never in a desktop session; XWayland, audible playback and the published linux-x64 folder were verified by reading only.
- Avalonia 11.3.22 on X11 has no XDND, so OS file drops do nothing on Linux; the UI points at Ctrl+I and the Import button there.
- Two instances sharing one cache can evict each other's proxies; the app detaches and re-queues lost proxies but does not lease artifacts.
- atempo alignment is calibrated with tone bursts (mean within 1 ms); a single click cannot be held to 1 ms by any constant compensation.
- M8 items are not built: reverse shuttle (J steps back one frame), per-track meters, hardware encoders, segmented export for very large timelines.
- The project schema stays at 1 through the effects batches: a build from before a batch opens a newer file and silently drops the fields it does not know (video and audio effects read as neutral, unknown transition kinds fall back to Crossfade). A file with a generator clip (effects batch 5) is refused by an older build with "Unknown clip type 'generator'".
- Generators: the bars are SMPTE HD bars at the canvas size whatever its aspect ratio. A scrub placeholder never shows a generator (it shows the topmost media clip's thumbnail until the exact frame arrives).
- Audio effects may click at playback window seams for very low cut-offs or a slow compressor release: the per-clip filters start cold at every window and the atempo pre-roll is not extended for them; exports are one continuous graph and are unaffected.
- Freeze frames are video only: the hold inserted by Shift+F has no audio partner. The linked audio is split at the frame together with the video (one split, so the pieces after the hold stay linked to each other) and the following audio moves with the hold like any insert, so the hold itself is silent (a gap on the audio track) and the sync after it is kept. The Freeze frame toggle on an existing clip holds its source in point, not the frame under the playhead (Shift+F does that), and a frozen frame cannot be slipped: freeze another frame instead.

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

Everything is committed and pushed: see the Repository section at the top.

---

**TLDR:** M0 to M7 are done (CI is green on Windows and Ubuntu), M8 is not started. Effects batches 1 to 5 and the folder split are merged on `main` (merge commit `d037c4c`), which is in sync with `origin/main`. The agent milestones A1 to A4 are done, including story 1 by hand with Claude Code against the published exe. On the merged tree the Release build, 1894 unit and 279 integration tests are green on this machine and on GitHub; the six smoke scripts (m9 included), the win-x64 publish and the serve demo were green on Windows before the merge and were not rerun here, and the publish folders are from 2026-10-22. Linux has run only on the CI runner, never in a desktop session. Provider neutrality (section 9): the discovery endpoints, the client profiles and three reference harnesses exist; the HTTP example is green against a headless host and `serve`, while the model-driven harness run is pending (user's request); the Python harnesses pass `py_compile` and the offline `check_harness.py` against the real served tool documents.
