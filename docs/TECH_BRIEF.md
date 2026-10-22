# Cutlass Technical Brief

Status: **normative for v1**. Date: 2026-10-21. Read it together with `docs/DESIGN.md`.

- This brief is for implementers who cannot ask questions. If something is unspecified, choose the simplest option consistent with it and note that in a code comment.
- Every graph and command line in it was run on ffmpeg N-126308 (libavfilter 12). Frame counts, sample counts, tags and byte sizes matched exactly.

## 0. Fixed constraints

- **Stack:** .NET 10 (SDK 10.0.401), C# `latest` (14).
- **Avalonia 11.3.22** for Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia.Fonts.Inter. Never Avalonia 12.
- **Packages:** CommunityToolkit.Mvvm 8.4.2, Silk.NET.OpenAL 2.23.0, Silk.NET.OpenAL.Soft.Native 1.23.1, xunit 2.9.3, Microsoft.NET.Test.Sdk 18.11.0, xunit.runner.visualstudio 4.0.0 (the pin used in the user's other repos).
- **ffmpeg and ffprobe** run as external processes only. Every filter and option used must exist in ffmpeg 6.0 (libavfilter 9).
- **Build:** `dotnet build Cutlass.slnx --disable-build-servers -m:1`.
- **No em dash characters** anywhere.

---

## 1. Solution layout

```
Cutlass/
  Cutlass.slnx  Directory.Build.props  Directory.Packages.props  .gitignore  CLAUDE.md
  docs/  build/publish.ps1  build/publish.sh  build/check-text.ps1 (fails on U+2014)  .github/workflows/ci.yml
  build/smoke.ps1  build/smoke/m4.smoke ... m7.smoke (the milestone smoke runs, section 10.4)
  build/publish.ps1 and build/publish.sh publish one RID self-contained into artifacts/publish/<rid> (nothing
  trimmed) and fail unless it holds the app, soft_oal.dll or libopenal.so and fonts/Inter-*.ttf with OFL.txt
  src/Cutlass.Core/       BCL only: time, model, edits, session/undo, serialization
  src/Cutlass.Compiler/   refs Core: pure timeline-to-ffmpeg compiler, presets, args (no I/O)
  src/Cutlass.Media/      refs Core+Compiler+Silk: processes, probe, cache, jobs, preview, export, audio
  src/Cutlass.App/        refs all + Avalonia + MVVM toolkit: WinExe, views, view models, controls
  src/Cutlass.Agent/      refs Core+Compiler+Media, never Avalonia: agent API model, lock, tools, HTTP host, headless host
  src/Cutlass.Agent.Mcp/  refs Agent + ModelContextProtocol: console exe, MCP over stdio (proxy or --headless), serve
  tests/Cutlass.Tests/             unit tests, no ffmpeg
  tests/Cutlass.IntegrationTests/  real ffmpeg, auto-skip when missing
```

**Agent integration (milestones A1 to A4).** `Cutlass.Agent` and `Cutlass.Agent.Mcp` are specified in `docs/AGENT_INTEGRATION.md`, which is normative for them.
- `Cutlass.App` references `Cutlass.Agent`.
- `ModelContextProtocol` 2.3.0 and its `Microsoft.Extensions.Hosting` dependency are pinned in `Directory.Packages.props` and referenced only by `Cutlass.Agent.Mcp` and the integration tests.
- The publish scripts place `Cutlass.Agent.Mcp` in the same folder as the app.

`Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework><LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings>
    <Deterministic>true</Deterministic><AnalysisLevel>latest</AnalysisLevel>
    <InvariantGlobalization>false</InvariantGlobalization>
    <Company>LouiDev</Company><Authors>LouiDev</Authors><Product>Cutlass</Product>
    <Copyright>Copyright (c) 2026 LouiDev</Copyright><Version>0.1.0</Version><NeutralLanguage>en</NeutralLanguage>
  </PropertyGroup>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

**`Directory.Packages.props`** sets `ManagePackageVersionsCentrally=true` and has one `PackageVersion` per package in section 0.

**Project settings:**
- **Cutlass.Media:** `AllowUnsafeBlocks=true`, for the Silk.NET pointer APIs and `LibraryImport`.
- **Cutlass.App:**
  - `OutputType=WinExe`, `AvaloniaUseCompiledBindingsByDefault=true`.
  - `ApplicationManifest=app.manifest` and `BuiltInComInteropSupport=true`, as in the Avalonia template.
  - `<Content Include="Assets/Fonts/*" CopyToOutputDirectory="PreserveNewest" Link="fonts/%(Filename)%(Extension)" />`.
  - `ApplicationIcon=Assets/cutlass.ico` for the exe, and the same file as `<AvaloniaResource>` for the window icon, which a `:is(Window)` style in `CutlassStyles.axaml` sets on every window.
  - Never reference an icon file that does not exist.
- **Test projects:** `IsPackable=false`, referencing Core, Compiler and Media.

**`Cutlass.slnx`** groups the projects in `<Folder Name="/src/">` and `<Folder Name="/tests/">`.

**Conventions:** file-scoped namespaces, one top-level type per file (partial classes may span `Type.Part.cs` files), namespaces follow folders.

---

## 2. File map

Each milestone (section 12) adds roughly 15 to 25 files. Files are grouped by project, with the milestone that creates them.

**Cutlass.Core**
- M1:
  - `Time/`: Rational, FrameRate, TimeMath, Timecode.
  - `Model/`: Project, Sequence, SequenceSettings, Track, TrackKind, Clip, MediaClip, TitleClip, StreamKind, VideoProps, Transform, ColorAdjust, AudioProps, VolumeKey, TitleSpec, TitleAnchor, Transition, TransitionKind, Marker, MediaAsset, MediaInfo, VideoStreamInfo, AudioStreamInfo.
  - `Editing/`: TimelineEdits (partials `.Place .Trim .Split .Delete .Transitions .Props .Tracks`), EditResult, EditMode, ClipEdge, ClipPlacement, TimelineValidator, TimelineQuery, SnapEngine, SnapResult, IIdGenerator, RandomIdGenerator, ClipClipboard.
  - `Session/`: ProjectSession, UndoHistory, HistoryEntry, ChangeKind, ProjectChangedEventArgs.
  - `Serialization/`: ProjectSerializer, ProjectJsonContext, ProjectMigrator, ViewState, `Dto/*`.

**Cutlass.Compiler**
- M3:
  - Root: RenderRequest, RenderPurpose, AudioLayout, CompiledRender, InputSpec, AuxFile, IMediaCatalog, MediaRenderInfo, RenderSource, FrameIndex.
  - `Planning/`: RenderPlanner, PlannedRun, PlannedSegment.
  - `Graph/`: FilterGraphBuilder, GraphChain, FfNum, FilterEscaper, VolumeEnvelope.
  - `Emit/`: VideoEmitter, AudioEmitter, TitleEmitter, TimelineCompiler, plus the internal EmitContext, InputTable, SegmentPlacement and VideoSegmentOutput.
  - `Export/`: ExportPreset, ExportPresetKind, ExportPresets, ExportQuality, ExportEncoding, EncoderTemplate, FfmpegArgsBuilder, OutputTail, OutputTailKind.
  - `Capabilities/`: FfmpegCapabilities, FilterScriptSyntax.

**Cutlass.Media**
- M2:
  - `Platform/`: AppPaths, WindowsJobObject, ChildProcessRegistry, FontLocator.
  - `Ffmpeg/`: FfmpegLocator, FfmpegToolset, CapabilityProbe, ProcessRunner, ProcessSpec, RunningProcess, FfmpegException, WorkDirectory.
  - `Parsing/`: ProbeParser, VersionParser, CapabilityListParser, ProgressParser, PacketIndexParser.
  - `Settings/`: AppSettings, SettingsStore. `Projects/`: ProjectStore. `Services/`: ProbeService.
- M3: `Export/`: ExportService, ExportJob, ExportJobState, ExportRequest, ExportProgress, FontResolvingCatalog. `Services/`: FrameIndexService (in memory; M5 adds the `frames.idx` disk cache), because exact export seeks need the index.
- M4: `Preview/`: FrameServer, FrameRequest, PreviewSize, IVideoFrameSink, VideoFrame. `Services/`: MediaCatalog, MediaImporter, PosterService.
- M5:
  - `Cache/`: MediaCache, Fingerprinter, CacheManifest, CacheArtifacts, CacheEntryUsage, ProxyMapping (plus the internal CacheJsonContext and ThumbnailIndexFile).
  - `Jobs/`: JobScheduler, MediaJob, JobKind, JobInfo, JobPriority, JobState, MediaPipeline (queues a media item's jobs and runs the LRU after each), plus the internal JobProcess.
  - `Services/`: ThumbnailService, ThumbnailSheet, WaveformService, PeakPyramid, ProxyService (FrameIndexService gains its disk cache).
  - `Preview/`: FrameCache, PlaybackSession, PlaybackOptions, PlaybackClock, VideoWindowReader, AudioFeeder, AudioMixer, MixerState, plus MixerTrack, MixerPeaks, PlaybackStats, PlaybackWindow (window planning) and the internal PlaybackRun, PlaybackPipeline, PlaybackServices, PcmBlock, PreviewRender and PreviewFontResolver.
  - `Audio/`: IAudioOutput, OpenAlAudioOutput, NullAudioOutput.
- M7: `Projects/`: MediaRelinker, RelinkScan, MediaLinkState, MediaFileFacts, AutosaveRecovery, RecoverableProject. `Platform/`: AppLog, ProcStat.
- M8: ReverseSession, SegmentedExport, HardwareEncoderProbe.

**Cutlass.App**
- M0: Program, App.axaml(+cs), AppServices, `Themes/CutlassTheme.axaml`, `Themes/Icons.axaml`, `Views/MainWindow.axaml`, MainWindowViewModel.
- M4:
  - `ViewModels/`: Welcome, MediaBin, MediaItem, Viewer, Timeline, Inspector, ExportDialog, Jobs.
  - `Views/`: WelcomeOverlay, MediaBinView, ViewerView, InspectorView, TimelineView, ExportDialog, JobsPopover, ErrorDialog.
  - `Controls/`: TimelineControl (`.Render`, `.Input`), TimelineViewState, HitTester, VideoSurface.
  - `Controls/Drag/`: IDragOperation, MoveClipsDrag, TrimDrag, MarqueeDrag, ScrubDrag.
  - `Services/`: IDialogService, AvaloniaDialogService, KeyBindingService, CommandRegistry, DefaultKeymap, FramePresenter.
  - Command line and smoke mode (section 10.4): `Services/` CommandLineOptions, SmokeScript, SmokeStep, SmokeRunner, SmokeLog, SmokeDialogService, plus `build/smoke.ps1` and `build/smoke/m4.smoke`.
- M5:
  - `Controls/`: AudioMeter, FilmstripCache, TrackGain (the header dB field), `TimelineControl.Media` (filmstrips and waveforms), `Drag/TrackGainDrag`.
  - `ViewModels/`: PlaybackController (the transport over `IPlaybackEngine`, unit tested with a fake engine).
  - `Services/`: IPlaybackEngine and PlaybackEngine (forwards to `PlaybackSession`), PlaybackPlanner and PlayRange (Space and Shift+Space ranges, shuttle speeds, which changes restart playback), MeterBallistics, IMediaArtifactSource and MediaArtifactSource, ThumbnailTile, `SmokeRunner.Playback`.
  - `build/smoke/m5.smoke`, run by `build/smoke.ps1 -Script m5` (section 10.4).
- M6:
  - Drags: RollDrag, SlipDrag, FadeHandleDrag, KeyframeDrag, VolumeLineDrag, MarkerDrag.
  - ScrubbyNumberBox.
  - Inspector sections: Clip, Title, Transition, Gap, Sequence.
  - EffectsViewModel and EffectsView, SpeedDialog, SequenceSettingsDialog.
  - Viewer: `ViewModels/SourceViewerViewModel` (Source mode), `Services/TimecodeEntry` (click-to-type timecode), `Services/SmokeRunner.Editing`, `Services/SmokeRunner.Inspect` and `build/smoke/m6.smoke`, run by `build/smoke.ps1 -Script m6` (section 10.4).
  - Drops: `Services/SourceDrag` and `SourceStreams` (the Source monitor drag payload), `ViewModels/TimelineViewModel.Drops` (Source and effect drops), `ViewModels/EffectDropPreview` (the effect drop ghost).
- M7: SettingsViewModel and SettingsDialog, RelinkDialog (with RelinkDialogViewModel and RelinkItemViewModel), `Services/` AutosaveService and RecoveryChoice, `ViewModels/MainWindowViewModel.Data` (autosave, recovery, relink on open, Clear cache), `App.Crash` (crash log and last autosave), `Views/WindowCapture` (window and dialog screenshots), `Services/SmokeRunner.Settings` and `Services/SmokeRunner.Dialogs` with `build/smoke/m7.smoke` and `build/smoke/m7b.smoke` (section 10.4), the root `README.md`. After the M7 review: `Services/PlatformFeatures` (where OS file drops work, section 10.1).

**Agent integration** (milestones A1 to A4; the full map and rules are in `docs/AGENT_INTEGRATION.md` section 2.1):
- **Core (A1):** `Session/ProjectSession.Group.cs` (groups and the write gate; ProjectSession becomes partial), `Session/SessionWriter`, `ChangeKind.GroupCommit` and `GroupCancel`, `Editing/TimelineEdits.Ranges.cs` (`RemoveRanges`).
- **Media (A2):** `Preview/StillRenderer`, `Preview/RangeAudioReader`, `Settings/AppSettings.Agent`, `AppPaths.AgentDirectory`.
- **Cutlass.Agent (A1, A2):**
  - `Api/`: DTOs, requests, `AgentJsonContext`, `ToolCatalog`, summary mapper and describer.
  - `Lock/`: `AgentLock` and its states.
  - `Tools/`: the tool groups, `EditRunner`, `PathPolicy`, `LevelAnalyzer`, `SilenceFinder`, the audit log and the event queue.
  - `Imaging/`: `PngEncoder`, `ContactSheetComposer`.
  - `Http/`: `AgentHttpServer`, auth, limits, token, endpoint file, `AgentClient`.
  - `Hosting/`: `IAgentHost`, `IAgentUiInvoker`, `IAgentServices`, and `HeadlessHost` with its dispatcher and engine.
- **Cutlass.App (A3):**
  - `Services/Agent/` (`AppAgentHost`, `AvaloniaUiInvoker`, `AgentServerController`).
  - `AgentViewModel`, `AgentCallItemViewModel`, and the partials `SettingsViewModel.Agents` and `MainWindowViewModel.Agent`.
  - `AgentBanner` and `AgentPanelView`, and the Agents page in `SettingsDialog`.
  - `CommandIds.AllowedWhileAgentHolds`, `SmokeRunner.Agent`, `build/smoke/m9.smoke`.
- **Cutlass.Agent.Mcp (A4):** `Program`, `McpOptions`, `ServerInstructions`, `Backend/` (HTTP and in-process backends, `EndpointLocator`), `Tools/CutlassTools`, `Resources/`, `Prompts/`.
- **Tests:** `tests/Cutlass.Tests/Agent/` (A1 to A3) and `tests/Cutlass.IntegrationTests/Agent/` (A2, A4).

---

## 3. Cutlass.Core

### 3.1 Time

- **Timeline positions** are integer frames.
- **Source positions** are flicks: a `long`, 705,600,000 per second, measured from the media's `format.start_time`. Flicks divide every supported rate exactly, including the /1001 rates.
- **Rounding.** Tick-to-flick conversions floor. Comparisons with frame-index timestamps use exact Int128 rational math.

```csharp
public readonly record struct Rational(long Num, long Den) : IComparable<Rational>   // normalized, Den > 0
{ public static Rational Create(long n, long d); public static Rational Parse(string s); // "30000/1001","30","1:1"
  public double ToDouble(); public string ToFfString(); public int CompareTo(Rational o); }  // Int128 compare
public readonly record struct FrameRate(Rational Value)
{ public static readonly ImmutableArray<FrameRate> Supported;   // 24000/1001,24,25,30000/1001,30,50,60000/1001,60
  public long FlicksPerFrame { get; } public static FrameRate Nearest(Rational measured); }
public static class TimeMath
{
    public const long FlicksPerSecond = 705_600_000; public const int SampleRate = 48_000;
    public static long SamplesAt(long frame, FrameRate r);        // floor(frame*48000*den/num), Int128
    public static long FrameAtSample(long sample, FrameRate r);   // largest f with SamplesAt(f) <= sample
    public static long SourceFlicksAt(MediaClip c, long frame, FrameRate r);
        // c.SourceInFlicks + FloorDiv((frame - c.Start) * r.FlicksPerFrame * c.SpeedPercent, 100)
    public static long FloorMicros(Rational seconds);
}
public static class Timecode { string Format(long frame, FrameRate r); bool TryParse(string s, FrameRate r, out long frame); }
```

### 3.2 Model (immutable records, `ImmutableArray<T>`)

```csharp
public sealed record Project(int SchemaVersion, ImmutableArray<MediaAsset> Media, Sequence Sequence);
public sealed record Sequence(SequenceSettings Settings, ImmutableArray<Track> Tracks, ImmutableArray<Marker> Markers,
    long? InFrame, long? OutFrame, bool HasHadClips);
public sealed record SequenceSettings(int Width, int Height, FrameRate Rate);      // audio fixed 48 kHz stereo
public sealed record Track(string Id, TrackKind Kind, string Name, bool Hidden, bool Locked, bool Targeted,
    bool Muted, bool Solo, double GainDb, ImmutableArray<Clip> Clips, ImmutableArray<Transition> Transitions);
public abstract record Clip(string Id, long Start, long Duration, bool Enabled, string? LinkId) { public long End => Start + Duration; }
public sealed record MediaClip(string Id, long Start, long Duration, bool Enabled, string? LinkId, string MediaId,
    StreamKind Stream, long SourceInFlicks, int SpeedPercent, VideoProps Video, AudioProps Audio) : Clip(Id, Start, Duration, Enabled, LinkId);
public sealed record TitleClip(string Id, long Start, long Duration, bool Enabled, TitleSpec Title, double Opacity,
    long FadeInFrames, long FadeOutFrames) : Clip(Id, Start, Duration, Enabled, null);
public sealed record VideoProps(Transform Transform, double Opacity, ColorAdjust Color, long FadeInFrames, long FadeOutFrames);
public sealed record Transform(double X, double Y, double Scale, double CropLeft, double CropTop, double CropRight,
    double CropBottom);   // X,Y px from canvas center; Scale relative to fit; crops are fractions 0..0.9
public sealed record ColorAdjust(double Brightness, double Contrast, double Saturation, double Gamma); // neutral 0,1,1,1
public sealed record AudioProps(double GainDb, long FadeInFrames, long FadeOutFrames, ImmutableArray<VolumeKey> Keys);
public sealed record VolumeKey(long Frame, double Db);      // Frame relative to clip Start
public sealed record TitleSpec(string Text, string Font, double Size, string Color, double OutlineWidth, string OutlineColor,
    bool Box, string BoxColor, double BoxPadding, TitleAnchor Anchor, double OffsetX, double OffsetY);
public sealed record Transition(string Id, TransitionKind Kind, string LeftClipId, string RightClipId, long Duration);
public sealed record Marker(string Id, long Frame, string Name, string Color);
public sealed record MediaAsset(string Id, string Path, string? RelativePath, string Fingerprint, long SizeBytes,
    DateTime MtimeUtc, MediaInfo Info);
public sealed record MediaInfo(long DurationMicros, long FormatStartMicros, string FormatName, bool IsStill,
    VideoStreamInfo? Video, AudioStreamInfo? Audio);
public sealed record VideoStreamInfo(int StreamIndex, string Codec, int Width, int Height, int DisplayWidth, int DisplayHeight,
    int Rotation, Rational Sar, Rational RFrameRate, Rational AvgFrameRate, Rational TimeBase, long StartPts, string PixFmt,
    bool IsVfr, bool IsHdr, long BitRate);
public sealed record AudioStreamInfo(int StreamIndex, string Codec, int SampleRate, int Channels);
// enums: TrackKind {Video,Audio}; StreamKind {Video,Audio}; TitleAnchor {Center,LowerThird,Top};
// TransitionKind {Crossfade,DipToBlack,DipToWhite,WipeLeft,WipeRight,SlideLeft,SlideRight,CircleOpen,AudioCrossfade}
```

**Ids** are a prefix (`m_ tr_ c_ x_ k_ l_`) plus 8 base32 characters, generated by `IIdGenerator`. Tests use a sequential generator.

**Transition geometry.** A transition of `d` frames on cut `c` has `a = d/2` (floor) and `b = d - a`, and covers the region `[c-a, c+b)`. The left clip renders `b` frames past its end and the right clip `a` frames before its start. A clip's **extent** is `[Start - headExt, End + tailExt)`.

### 3.3 Edits, validation, queries, snapping

All edits live in `public static partial class TimelineEdits`. Each returns `EditResult(Project Project, string Label, ImmutableArray<string> Warnings, ImmutableArray<string> Select)`.

**Behaviour rules:**
- Edits never throw on user input. They clamp and add a warning.
- An unchanged result returns the same `Project` reference, and the session does not record it.
- Edits that create ids take an `IIdGenerator` first.

**Functions:**
- `Place(clips, at, EditMode)`: drops, paste, three-point edits.
- `Move(ids, dFrames, dTracks, mode)`, `Nudge`.
- `Trim(clipId, edge, delta, ripple, includeLinked)`, `Roll(leftId, rightId, delta)`, `Slip(clipId, delta, includeLinked)`, `RippleTrimToFrame` (Q/W).
- `Split(trackIds, frame, onlyClipIds?)`, `Lift`, `RippleDelete`, `CloseGap(trackId, gapStart)`.
- `SetSpeed(clipIds, percent, rippleFollowing)` (several clips as one step), `UpdateClips(ids, Func<Clip,Clip>, label)`, `SetEnabled`, `Link`, `Unlink`.
- `AddTransition` (and `AddTransitions` for several cuts), `SetTransitionDuration`, `SetTransitionKind`, `RemoveTransitions`.
- `SetTrack`, `AddTrack`, `RemoveTrack`, `NewProject` (V1, V2, A1, A2; V1 and A1 targeted).
- Marker edits, `SetInOut`.
- `ImportMedia`, `RemoveMedia`, `SetSequenceSettings` (converts positions by time).
- Helpers: `SourcePlacements` (a null track leaves that stream out), `SourceRangeFrames` (the timeline length of a source range) and `SourceEdit` (drops and three-point edits), `SplitLinked` (razor click), `MoveIntoSync`, and `ClipClipboard` (copy, paste).

**Resolved details:**
- **Split.** Left halves keep their id and link group; right halves get new ids and one new link group per original group.
- **Ripple on other tracks** (ripple trim, ripple delete, close gap, speed): a track without room to close up is left alone, with a warning.
- **Transitions** are re-clamped after every edit, earlier cuts first. Slip is limited so the clip's extent stays inside its source; trims and rolls shorten transitions instead.

**`TimelineValidator.Check(Project)`** returns a list of violations. It runs after every edit in DEBUG builds and in tests. The invariants:
1. Clips are sorted, never overlap, and have `Duration >= 1`.
2. Track kind matches the clip: video tracks hold video MediaClips and TitleClips, audio tracks hold audio MediaClips. Referenced media exists and has the stream.
3. A media clip's extent lies inside its source. Stills and titles are unbounded.
4. A transition joins two butting clips on its own track, with `d >= 2`, `a <= left.Duration` and `b <= right.Duration`. Two transitions on one clip never overlap.
5. Speed is 25 to 400. `FadeIn + FadeOut <= Duration`. Keyframes lie in `[0, Duration)`.
6. At most 8 video and 8 audio tracks.

Linked clips have **no** equality constraint. A link is only a group id.

**Queries and snapping:**
- `TimelineQuery`: ClipsAt, EditPoints, Duration, Gaps, Runs (maximal butting runs of enabled clips), LinkGroup.
- `SnapEngine.Collect(project, playhead, excludeIds)` returns the targets.
- `SnapEngine.Resolve(movingEdges, delta, targets, pixelsPerFrame, thresholdPx = 8)` returns `SnapResult(Delta, SnapFrame?)`.

### 3.4 Session and undo

```csharp
public sealed record HistoryEntry(Project Before, Project After, string Label, string? CoalesceKey, long TimestampMs);
public sealed class ProjectSession                                   // UI thread only
{
    public ProjectSession(Project initial, Func<long> clockMs);
    public Project Current { get; }  public bool IsDirty { get; }    // !ReferenceEquals(Current, saved)
    public string? UndoLabel { get; } public string? RedoLabel { get; }
    public event EventHandler<ProjectChangedEventArgs>? Changed;      // Old, New, ChangeKind
    public EditResult Execute(Func<Project, EditResult> edit, string? coalesceKey = null);
    public void BeginGesture(); public EditResult UpdateGesture(Func<Project, EditResult> fromGestureStart);
    public void CommitGesture(string label); public void CancelGesture();
    public bool Undo(); public bool Redo(); public void MarkSaved(); public void Replace(Project loaded);
}
```

- **Gestures.** `Current` changes live during a gesture (`ChangeKind.GestureUpdate`) and commits as one entry whose `Before` is the gesture start.
- **Coalescing.** An `Execute` call with the same coalesce key as the top entry within 1.5 s merges into it, keeping the older `Before`. Example keys: `inspector:<clipId>:color.saturation`, `nudge:<selectionHash>`.
- **Limits.** 500 entries. A new edit clears the redo stack.
- **Not undoable.** Selection and view state.
- **Groups and the write gate (A1, AGENT_INTEGRATION 2.6).** `BeginGroup`/`EndGroup`/`CancelGroup` collect edits into one entry (`ChangeKind.GroupCommit`, `GroupCancel`); inside a group nothing coalesces and undo is refused. `WriteGate` plus `WriteAs(SessionWriter)` refuse user writes while an agent holds the lock (`WriteRefused`); `MarkSaved` is always allowed.

### 3.5 Serialization

- **Format.** System.Text.Json with a source-generated `ProjectJsonContext`, camelCase, indented, UTF-8 without BOM, `\n` line endings on every OS, and the relaxed encoder so non-ASCII text stays literal.
- **DTOs.** The serializer maps to and from DTOs, so the model has no JSON attributes. Unknown fields are ignored.
- **Migration.** `ProjectMigrator.Migrate(JsonNode)` upgrades old `schemaVersion`s. Schema 0 (files without `schemaVersion`) gains `format`, renames `fps` to `frameRate`, and adds missing `markers`, `transitions` and clip `type`. A newer schema is refused.
- **Paths** use forward slashes.
- **Atomic save.** `ProjectStore` writes a unique temp file `<path>.<pid>.<8 hex>.tmp` (`ProjectStore.TempPath`), flushes, then calls `File.Move(tmp, path, overwrite: true)`. `SettingsStore` (`settings.json`) and `RecentProjectsStore` (`recent.json`) use the same temp name, so two instances saving at once never write into one temp file.

```json
{ "format": "cutlass-project", "schemaVersion": 1, "app": "Cutlass 0.1.0",
  "media": [{ "id": "m_7f2c4k9q", "path": "D:/Footage/beach.mp4", "relativePath": "../Footage/beach.mp4",
    "fingerprint": "sha256:9f2c41d07a...", "sizeBytes": 734003200, "mtimeUtc": "2026-10-01T09:12:44Z",
    "info": { "durationUs": 64200000, "formatStartUs": 0, "formatName": "mov,mp4,m4a,3gp,3g2,mj2", "still": false,
      "video": { "index": 0, "codec": "hevc", "width": 3840, "height": 2160, "displayWidth": 3840, "displayHeight": 2160,
        "rotation": 0, "sar": "1/1", "rFrameRate": "30000/1001", "avgFrameRate": "30000/1001", "timeBase": "1/30000",
        "startPts": 0, "pixFmt": "yuv420p10le", "vfr": false, "hdr": false, "bitRate": 48000000 },
      "audio": { "index": 1, "codec": "aac", "sampleRate": 48000, "channels": 2 } } }],
  "sequence": { "width": 1920, "height": 1080, "frameRate": "30000/1001", "sampleRate": 48000,
    "inFrame": null, "outFrame": null, "hasHadClips": true,
    "markers": [{ "id": "k_3m9q0aa1", "frame": 300, "name": "Drop", "color": "#F5D547" }],
    "tracks": [
      { "id": "tr_v1", "kind": "video", "name": "V1", "hidden": false, "locked": false, "targeted": true,
        "clips": [
          { "type": "media", "id": "c_01", "stream": "video", "mediaId": "m_7f2c4k9q", "start": 0, "duration": 150,
            "enabled": true, "linkId": "l_01", "sourceInFlicks": 1411200000, "speedPercent": 100,
            "video": { "transform": { "x": 0, "y": 0, "scale": 1.0, "crop": [0, 0, 0, 0] }, "opacity": 1.0,
              "color": { "brightness": 0, "contrast": 1, "saturation": 1, "gamma": 1 }, "fadeIn": 0, "fadeOut": 0 } },
          { "type": "title", "id": "c_02", "start": 300, "duration": 90, "enabled": true,
            "title": { "text": "Day One", "font": "builtin:Inter-Bold", "size": 96, "color": "#FFFFFFFF",
              "outlineWidth": 3, "outlineColor": "#00000099", "box": false, "boxColor": "#00000080",
              "boxPadding": 16, "anchor": "lowerThird", "offsetX": 0, "offsetY": 0 },
            "opacity": 1.0, "fadeIn": 15, "fadeOut": 15 } ],
        "transitions": [{ "id": "x_01", "kind": "crossfade", "left": "c_01", "right": "c_05", "duration": 30 }] },
      { "id": "tr_a1", "kind": "audio", "name": "A1", "muted": false, "solo": false, "locked": false, "targeted": true, "gainDb": 0,
        "clips": [{ "type": "media", "id": "c_11", "stream": "audio", "mediaId": "m_7f2c4k9q", "start": 0, "duration": 150,
          "enabled": true, "linkId": "l_01", "sourceInFlicks": 1411200000, "speedPercent": 100,
          "audio": { "gainDb": 0, "fadeIn": 0, "fadeOut": 12, "keys": [{ "frame": 90, "db": 0 }, { "frame": 120, "db": -12 }] } }],
        "transitions": [] } ] } }
```

**Sidecar.** `<name>.cutlass.user` holds `{ pixelsPerFrame, scrollFrame, scrollY, playhead, trackHeights, previewQuality, useProxies, binView }`. Writing it never dirties the project.

---

## 4. Threading model

| Thread | Owns / does |
|---|---|
| UI | `ProjectSession`, view models, Avalonia objects, every `WriteableBitmap` Lock/copy, presenter tick (`TopLevel.RequestAnimationFrame`) |
| FrameServer worker | loop over `Channel<FrameRequest>` (bounded 1, DropOldest): compile, spawn, read, `IVideoFrameSink.Present` |
| VideoWindowReader | per playback session: rawvideo frames into a bounded queue of 8 pooled buffers |
| AudioFeeder | per playback session: reads s16le, mixes, makes every OpenAL call while playing, publishes the clock |
| JobScheduler | tasks under a semaphore (2 background processes, 1 while playing or exporting) |
| Export | one task per job, progress through `IProgress<ExportProgress>` |
| Process I/O | one stderr drain task per process |

**Rules:**
- Workers receive immutable `Project` snapshots and never lock them.
- Media and Compiler never reference Avalonia.
- Never block the UI thread on a process.
- Buffers come from `ArrayPool<byte>.Shared`, except cached frames, which own their arrays.
- OpenAL calls never overlap: `OpenAlAudioOutput` serialises every call under one lock, and its context is current for the whole process (`alcMakeContextCurrent`, not a thread-local context). While a run plays, only its AudioFeeder thread calls the output. The run's startup task calls `Open` before the feeder starts, its teardown task calls `Stop` and `Flush` after the feeder thread ended, and a new run waits for the previous teardown. The UI thread reads only counters (`Underruns`, `IsAvailable`), which make no AL call.

---

## 5. Process layer and platform rules

### 5.1 Paths (`AppPaths`)

| | Windows | Linux |
|---|---|---|
| Settings | `%APPDATA%\Cutlass\settings.json` | `${XDG_CONFIG_HOME:-~/.config}/cutlass/settings.json` |
| Data (recent.json, `autosave/untitled-<id>.cutlass`, `logs/cutlass-<date>.log`) | `%LOCALAPPDATA%\Cutlass\` | `${XDG_DATA_HOME:-~/.local/share}/cutlass/` |
| Cache root | `%LOCALAPPDATA%\Cutlass\Cache\v1\` | `${XDG_CACHE_HOME:-~/.cache}/cutlass/v1/` |

- Resolve the XDG variables explicitly: `LocalApplicationData` is not the cache on Linux.
- Normalise with `Path.GetFullPath`.
- Compare paths case-insensitively only on Windows.
- Project files store media paths with `/` on every OS; `ProjectStore.LoadAsync` converts them to the native separator.
- `AppLog` keeps one file per UTC day in `logs/`, at most 1 MiB each, and the newest 14 files. It records unhandled and unobserved exceptions (the crash hooks also write one last autosave).

### 5.2 Locating ffmpeg and capabilities

**`FfmpegLocator` search order:**
1. The settings path.
2. `<appdir>/ffmpeg/`, then `<appdir>`.
3. Each `PATH` entry: `ffmpeg.exe` on Windows, `ffmpeg` with an execute bit on Linux (`File.GetUnixFileMode`).
4. Well-known folders:
   - Windows: `%LOCALAPPDATA%\Microsoft\WinGet\Links`, `%USERPROFILE%\scoop\shims`, `C:\ProgramData\chocolatey\bin`, `C:\ffmpeg\bin`.
   - Linux: `/usr/bin`, `/usr/local/bin`, `/opt/ffmpeg/bin`. Not `/snap/bin`: the snap's ffprobe is named `ffmpeg.ffprobe`, and a confined snap cannot read `~/.cache`, where the work directory lives.

`ffprobe` must sit next to the chosen ffmpeg.

On Linux a picked or bundled build that lost its execute bits (unzipped) gets them back before validation (`chmod +x` like a 022 umask: the owner, plus group and others where they may read; only files the user owns change); system folders are never changed. `FfmpegLocator.EnsureExecutable(path)` is this rule in one call (system folders: `/usr`, `/bin`, `/sbin`, `/lib`, `/lib64`, `/etc`, `/snap`, `/nix`); the settings dialog uses it for a typed path.

**Validation.** Run `-hide_banner -nostdin -version` (the 5.3 stdin rule applies to every non-export ffmpeg run). Parse the build id with `^ffmpeg version (\S+)` and the libavfilter version with `^libavfilter\s+(\d+)\.\s*(\d+)`. Never parse semver, because builds print `N-126308`. Require libavfilter major `>= 9`.

**`CapabilityProbe`** caches its results in `capabilities.json`, keyed by path, size and mtime.
- **Lists.** Encoders come from `-encoders` and filters from `-filters`. A filter line is one whose first token contains only `TSC.` and whose third token contains `->`. The name is the second token.
- **Required filters:** color, anullsrc, nullsrc, trim, atrim, setpts, asetpts, fps, tpad, apad, concat, xfade, overlay, scale, pad, crop, setsar, setparams, format, aformat, aresample, atempo, afade, fade, volume, amix, amerge, adelay, eq, lut, framestep, alimiter, split, palettegen, paletteuse, tile, settb, anull.
  - If any is missing, the status is red and preview and export are disabled.
  - `drawtext` is optional. Without it, titles are disabled with a banner.
- **Script syntax.** Write `nullsrc=s=16x16:d=0.04[v]` to `probe.ffg`, then:
  1. Run `ffmpeg -hide_banner -nostdin -v error -/filter_complex probe.ffg -map [v] -f null -`. Exit 0 means `Slash`.
  2. Otherwise, if the libavfilter major is 9, retry with `-filter_complex_script`. Exit 0 means `LegacyScript`.
  3. Otherwise `InlineOnly`.

  Master has removed `-filter_complex_script`, and a graph without a mapped output always fails.
- **Encoder time base keyword:** `demux` when libavfilter >= 10, else `-1`.

### 5.3 ProcessRunner

```csharp
public sealed record ProcessSpec(string FileName, IReadOnlyList<string> Args, string WorkingDirectory,
    bool RedirectStdin, ProcessPurpose Purpose, bool BelowNormalPriority);
public sealed class RunningProcess : IAsyncDisposable
{
    public int Pid { get; } public string DisplayCommand { get; }
    public Stream Stdout { get; }                        // StandardOutput.BaseStream, raw bytes
    public Task<int> Exited { get; }
    public IReadOnlyList<string> StderrTail(int lines);  // ring of the last 300 lines
    public Task RequestQuitAsync();                      // "q\n" on stdin (exports)
    public Task KillAsync();                             // Kill(entireProcessTree: true)
}
public sealed class ProcessRunner { public RunningProcess Start(ProcessSpec spec); }
```

**Rules:**
- `UseShellExecute=false` and `CreateNoWindow=true`. Redirect stdout and stderr, plus stdin for exports. `WorkingDirectory` is the work directory.
- Arguments go through `ArgumentList` only.
- Stderr is always drained.
- Every ffmpeg process except an export gets `-nostdin`. Exports must not, because `q` is their graceful stop. ffprobe never reads stdin and rejects `-nostdin` ("Option not found"), so it never gets it.
- Background jobs run at `BelowNormal` priority, inside a try/catch.
- `DisplayCommand` (POSIX quoting on Linux, double quotes on Windows) is for display only.

### 5.4 Orphan control

- **Windows:** `WindowsJobObject`, guarded by `OperatingSystem.IsWindows()`.
  - Create one job with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (0x2000) via `CreateJobObjectW` and `SetInformationJobObject` (`JobObjectExtendedLimitInformation`).
  - Call `AssignProcessToJobObject` for every child and keep the handle for the app's lifetime.
- **Linux:** ffmpeg ignores SIGPIPE and stops only when writing its own output fails with EPIPE.
  - Preview processes writing to stdout therefore die with the parent.
  - File writers (proxy, thumbnails, export) do not, even with `-progress pipe:1`.
  - So `ChildProcessRegistry` writes this instance's own file `pids-<ownerPid>-<ownerStartTicks>.json` in the cache root (owner pid, owner start time, `/proc/sys/kernel/random/boot_id`, and per child pid, start time, executable), at most twice a second. The snapshot is taken under the lock and written outside it, temp-then-rename without fsync (after a power loss the children are dead anyway); without children the file is deleted. On Linux a start time is field 22 of `/proc/<pid>/stat` (clock ticks since boot, parsed after the last `)` because the name may hold spaces), which stays stable across sessions; `Process.StartTime` adds an estimated boot time that moves with wall clock corrections.
  - At startup it sweeps the other instances' files (`ChildProcessRegistry.Decide`): a file whose owner still runs (pid and start time within 20 ms) is left alone, so a second instance never kills the first one's live children; a file from another boot (boot ids differ) or an unreadable one is only deleted; otherwise it kills the recorded processes whose start time matches within 20 ms and whose name starts with `ffmpeg` or `ffprobe`, and deletes the file. A legacy shared `pids.json` names no owner and is deleted without killing anything.
- **Both OSes:** kill all children on shutdown, in `AppDomain.ProcessExit` and in the unhandled-exception handler. Delete leftover `*.tmp-*` artifacts at startup.

### 5.5 Work directory, fonts, escaping

**Work directory.** Every ffmpeg process runs in `<cache>/work/`. It holds:
- `Inter-Regular.ttf`, `Inter-SemiBold.ttf`, `Inter-Bold.ttf`, copied from `<appdir>/fonts/` when missing or of a different size.
- `font_<sha8>.<ext>`: user fonts, copied once.
- `t_<sha8>.txt`: title text, UTF-8 without BOM and without a trailing newline.
- `g_<sha12>.ffg`: scripts.

Files are content-addressed and written temp-then-rename, so app instances can share them. `.txt` and `.ffg` files older than two days are deleted at startup.

**Escaping.** Graphs reference files only by bare names matching `^[A-Za-z0-9_.-]+$`, and `FilterGraphBuilder` throws otherwise. No drive colons, apostrophes or Unicode ever appear inside a graph. Input and output paths are absolute and go through `ArgumentList`.

**Fonts.**
- Static Inter TTFs and `OFL.txt` from the official Inter 4.x release (`extras/ttf/`) are committed to `Cutlass.App/Assets/Fonts/`. Never extract them from Avalonia.Fonts.Inter, which only provides the UI font.
- **Fallbacks** (the title shows a warning badge):
  - Windows: `C:/Windows/Fonts/segoeui.ttf`, then `arial.ttf`.
  - Linux: `fc-match -f %{file} sans-serif`, then `/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf`.
  - Last resort on both: the bundled `Inter-Regular.ttf`.
- **User font paths** come from project files, so `FontLocator` reads one only when it is a regular file with a `.ttf`, `.otf` or `.ttc` extension of at most 64 MiB (`IsAcceptableFontFile`). Devices, FIFOs, folders, huge and unreadable files take the fallback with its warning; file system errors never escape. `PreviewFontResolver` remembers failures, so a bad path is not retried for every frame.
- **System font list** (`FontLocator`):
  - Windows: scan `%WINDIR%\Fonts` and `%LOCALAPPDATA%\Microsoft\Windows\Fonts` for `*.ttf` and `*.otf`.
  - Linux: `fc-list --format "%{family[0]}|%{file}\n"`, else scan `/usr/share/fonts`, `/usr/local/share/fonts` and `~/.local/share/fonts`.

### 5.6 OpenAL, dialogs

**OpenAL:**
- Load with `ALContext.GetApi(soft: true)` and `AL.GetApi(soft: true)`. The Soft.Native package provides `runtimes/win-x64/native/soft_oal.dll` and `runtimes/linux-x64/native/libopenal.so`, resolved through deps.json.
- Open the configured or default device. Create one context, one source and 6 buffers, `Stereo16` at 48 kHz.
- On failure, retry with `soft: false`, then fall back to `NullAudioOutput` (Stopwatch clock) and show "Audio unavailable".
- **Disconnect.** With `ALC_EXT_disconnect`, every service pass reads `ALC_CONNECTED` (0x313). A disconnected device makes `IsAvailable` false, freezes `PlayedSamples` (it raced ahead and fast-forwarded the video), refuses buffers and raises `Disconnected`. The playback feeder then continues on a Stopwatch clock from the last real position (`AudioClockSource`), drops the PCM blocks at the rate they would have played, and the session raises `AudioUnavailable` for the status bar. The next `Open` (the next play) tries `alcReopenDeviceSOFT` when `ALC_SOFT_reopen_device` exists: the configured device, then the default one.
- Devices are listed with `ALC_ALL_DEVICES_SPECIFIER`.

**Dialogs:**
- `AvaloniaDialogService` uses `TopLevel.StorageProvider.OpenFilePickerAsync`, `SaveFilePickerAsync` and `OpenFolderPickerAsync`, plus `TryGetLocalPath()`. A null local path shows "Only local files are supported".
- Drag and drop uses `DragDrop.DropEvent` with `DataFormats.Files`. Known Linux limitation: Avalonia 11.3's X11 backend has no XDND, so OS file drops do not arrive on Linux (X11 and XWayland); import goes through Ctrl+I, the + button and the welcome "Import media..." button there (section 10.1). Drags inside the app work everywhere.

---

## 6. Media services

### 6.1 Cache layout

```
<cache>/v1/media/<fp[0..2]>/<fp>/
  manifest.json  { fingerprint, ffmpegBuild, artifacts:{probe:1,index:1,poster:1,thumbs:1,peaks:1,proxy540:1},
                   proxyMapping:"exact"|"ordinal"|"rejected", lastAccessUtc }
  probe.json     raw ffprobe output
  frames.idx     "CTLX" u16 ver, i64 tbNum, i64 tbDen, i32 count, i64 pts[count] ascending
  poster.jpg     thumbs/sheet_001.jpg..  thumbs/thumbs.json {intervalSec, tileW:160, tileH:90, cols:10, rows:10, count}
  peaks.bin      "CTLP" u16 ver, i32 peaksPerSecond(100), i32 levels, per level: i32 n, (i16 min,i16 max)[n]
  proxy540.mov   proxy540.idx (frames.idx format)
<cache>/v1/work/   <cache>/v1/pids-<ownerPid>-<ownerStartTicks>.json (Linux, one per running instance)
```

- Artifacts are written as `name.tmp-<guid8>.<ext>` (a directory for thumbnails) and renamed, then recorded in the manifest.
- Version bumps invalidate only the artifact concerned.
- A `frames.idx` with count 0 records "no usable index" (the CFR fallback), so that result is cached too. The disk index is used only while the file still has the size and mtime recorded in the asset.
- A rejected proxy is recorded as artifact `proxy540` with `proxyMapping:"rejected"` and no files, so it is retried only after a version bump.
- `peaks.bin` is mono (the decode uses `-ac 1`); `PeakPyramid.PeaksFor` takes 48 kHz sample positions from the file start.
- An LRU on `lastAccessUtc` runs at startup and after each job, down to the limit (20 GB). Media used by the open project is never evicted.
- "Clear cache" (`MediaCache.ClearAsync`, through `MediaPipeline.ClearCacheAsync`) deletes every media folder except those of media a running job works on and those holding a fresh temp; each folder loses its manifest first. Proxies of cleared media are detached, and the open project's jobs are queued again.
- **Several instances** share the cache, but each keeps its own catalog: one instance's LRU or "Clear cache" can delete artifacts another still points at. Only the cheap half is handled: a scrub render that fails while proxies are on detaches every proxy whose file is gone (`MediaCatalog.DetachMissingProxies`) and renders again from the originals, a playback start detaches them before compiling, and `MediaPipeline` queues each lost proxy again (`ProxyLost`). Frame indexes and peaks live in memory once loaded, and a missing file on disk only means it is rebuilt. Thumbnail sheets read after a foreign clear show no tiles until the thumbnail job runs again.
- **Versions.** `index` is 2 (no dts-built index for packets without pts, 6.4) and `peaks` is 2 (peaks start at `format.start_time`, 6.5); the others are 1.
- `probe.json` is read back when the fingerprint (from the `fingerprints.json` memo, so unchanged files are not rehashed) has a current `probe` artifact; a re-import or reopen then skips ffprobe.

### 6.2 Fingerprint

- `sha256:` plus the hex SHA-256 over:
  - the file size as 8 bytes little-endian
  - the first, middle and last 1 MiB of the file (the whole file when it is under 3 MiB); the middle chunk starts at `(size - 1 MiB) / 2`
- Path and mtime are excluded, so caches survive renames and relink can match by content.
- A memo in `fingerprints.json`, keyed by `path|size|mtimeTicks`, avoids rehashing.

### 6.3 Probe

`ffprobe -v error -print_format json -show_format -show_streams "<file>"` with a 15 s timeout. `ProbeParser` reads it as follows:

- **Streams.** The video stream is the first `video` stream without `disposition.attached_pic=1`. The audio stream is the first audio stream; only that one is used in v1.
- **Rotation** comes from `side_data_list[].rotation`, else `tags.rotate`. At ±90/270 the width and height swap. `VideoStreamInfo.Rotation` is stored clockwise in {0, 90, 180, 270}: the display matrix angle is counter-clockwise and is negated, `tags.rotate` is already clockwise (a phone portrait clip reads -90 or 90 and stores 90). ffmpeg autorotates on decode, so **never pass `-noautorotate` and never add `transpose`**.
- **Display width** is `round_even(width * SAR)`. A SAR of `0:1` counts as 1.
- **Flags:**
  - VFR when `r_frame_rate` and `avg_frame_rate` differ by more than 1%.
  - HDR when `color_transfer` is `smpte2084` or `arib-std-b67`.
  - Still when the codec is png, mjpeg, webp, bmp or tiff and either `nb_frames <= 1` or the format contains `image2` or `_pipe`.
- **Times.** `format.start_time` becomes `FormatStartMicros`. Duration is `format.duration`, else the stream duration.

### 6.4 Frame index

```
ffprobe -v error -select_streams <videoStreamIndex> -show_entries packet=pts,dts,flags -of csv=p=0 "<file>"
```

This only demuxes, so it is fast. Processing:
- Drop lines whose flags contain `D`.
- Use `dts` when `pts` is `N/A`, but for at most 4 packets: with more, store no index (CFR fallback). In AVI with B-frames (mpeg4 `-bf 2`) a third of the packets lack a pts and their dts ran the index one frame ahead of the decoded timestamps, so the proxy also classified `exact` while showing another frame.
- Sort ascending and remove duplicates.
- If more than 5% of lines are unusable (no parseable `pts` or `dts`, or malformed; dropped `D` packets do not count), store no index. The CFR fallback (section 7.2) is used and the media shows a "VFR-unsafe" tooltip.

### 6.5 Poster, thumbnails, waveform

```
ffmpeg -hide_banner -nostdin -v error -ss <10% of duration> -i "<file>" -map 0:<v> -frames:v 1
       -vf "scale=w=320:h=180:force_original_aspect_ratio=decrease" -q:v 4 -y poster.tmp-1a2b3c4d.jpg
ffmpeg -hide_banner -nostdin -v error [-skip_frame nokey if > 10 min] -i "<proxy or file>" -map 0:<v> -an -sn
       -vf "fps=1/<interval>:round=up,scale=160:90:force_original_aspect_ratio=decrease,pad=160:90:(ow-iw)/2:(oh-ih)/2,tile=10x10"
       -q:v 5 -progress pipe:1 -nostats -y "thumbs.tmp-1a2b3c4d/sheet_%03d.jpg"
ffmpeg -hide_banner -nostdin -v error -i "<file>" -map 0:<a> -vn -af aresample=async=1:first_pts=0 -ac 1 -ar 16000 -f s16le pipe:1
```

- **Poster.** `PosterService` writes the media folder's `poster.jpg` artifact (temp, then commit). A poster left at the M4 location `<cache>/posters/<fingerprint hex>.jpg` (the hex after `sha256:`, since a colon is not a valid Windows file name) is still read and moved into the media cache on first use; without a media cache (tests) that folder is used. Stills take no `-ss`, and when the seek yields no frame (a duration past the last frame) the poster retries from the start. Audio-only media has no poster.
- **Thumbnails.** `interval = max(1 s, duration/600)` and `count = ceil(duration/interval)`. Thumbnail `i` shows source time `i*interval`, and lookups clamp. This `pad` is safe because the scaled image never exceeds 160x90.
  - `round=up` is required: the fps filter keeps the last input frame that rounds into each output slot, so the default `round=near` showed the frame just before `(i+0.5)*interval`. With `round=up` thumbnail `i` is the frame with the largest pts at or before `i*interval`, the compiler's sample-and-hold rule (verified on the 25 fps oracle: tiles show frames 0, 25, 50, ...).
  - A duration a little past a whole interval can add one trailing tile beyond `count`. Lookups never reach it.
- **Waveform.** C# takes min/max over 160-sample blocks, giving 100 peaks per second, then builds halving mip levels. Progress is bytes read against `duration*32000`. `aresample=async=1:first_pts=0` pads audio that starts after `format.start_time` with silence, so peak 0 is the origin of source time and the waveform lines up with what is heard (audio starting 0.5 s late peaks at 50, not 0).

### 6.6 Proxies

Proxies are intra-only MJPEG (LGPL-safe), 540 lines, video only. Preview audio always comes from the original.

```
ffmpeg -hide_banner -nostdin -v error -y -i "<file>" -map 0:<v> -an -sn -fps_mode passthrough
       -enc_time_base <demux|-1> -video_track_timescale <source tb den>
       -vf "scale=w=-2:h=540:flags=bilinear,format=yuvj420p" -c:v mjpeg -q:v 4 -progress pipe:1 -nostats "proxy540.tmp-1a2b3c4d.mov"
```

`-enc_time_base` is passed for every source, not only flagged VFR ones: the VFR flag (section 6.3) misses VFR files whose average rate equals the nominal rate, and without it their proxy timestamps were rounded to 1/R, which mapped only by ordinal and put sample-and-hold one frame off.

**After encoding**, `ProxyService` indexes the proxy (section 6.4):
- If the frame count differs from the source, the proxy is `rejected` and the original stays in use.
- If every pts equals the source pts (minus each file's format start), it is `exact`.
- Otherwise it is `ordinal`.

**Seeking** always takes an ordinal from the source index and that ordinal's pts from the file actually used. CFR sources must map `exact` (integration test).

**Auto policy:** build a proxy when the display height is above 1080, the codec is hevc, av1 or vp9, the source is VFR, or the bitrate is above 50 Mb/s.

### 6.7 JobScheduler

- **Job order per media:** Probe, Index, Poster, Thumbnails, Waveform, Proxy. Probe and Poster run inside the import (`MediaImporter`), before a media item exists, so `JobKind` holds only FrameIndex, Thumbnails, Waveform and Proxy, and the Jobs popover lists those plus exports (the bin card shows its spinner during the import).
- **Priorities:** Visible (clips on screen or selected in the bin) runs before Background. Scrub and playback bypass the scheduler. M5 scope: imports and "Create proxy" queue as Visible and a project load queues as Background; raising a job when its clip scrolls into view or its card is selected is not done yet.
- **Concurrency:** 2 background processes, 1 during playback. An export cancels a running proxy and re-queues it.
- **Restart:** unfinished jobs for the open project are re-queued on launch.
- **Events:** each job publishes `JobInfo` (kind, media, fraction, state, error). `ArtifactReady(mediaId, kind)` is raised on the UI thread.

---

## 7. The compiler (timeline to filtergraph)

### 7.1 API

```csharp
public enum RenderPurpose { Export, PreviewFrame, PreviewVideo, PreviewAudio }
public enum AudioLayout { Mixed, PerTrack }
public sealed record RenderRequest(long StartFrame, long EndFrame, int CanvasWidth, int CanvasHeight, RenderPurpose Purpose,
    bool IncludeVideo, bool IncludeAudio, bool UseProxies, AudioLayout AudioLayout, int FrameStep, OutputTail Tail, bool LimitPeaks);
public sealed record CompiledRender(IReadOnlyList<InputSpec> Inputs, string GraphText, string? VideoLabel, string? AudioLabel,
    long FrameCount, long SampleCount, int AudioChannels, IReadOnlyList<AuxFile> AuxFiles, IReadOnlyList<string> Warnings);
public sealed record InputSpec(string Path, long? SeekMicros, long DurationMicros, bool LoopStill, Rational? StillRate);
public sealed record AuxFile(string FileName, string Utf8Content);
public interface IMediaCatalog { MediaRenderInfo? Get(string mediaId); string? ResolveFont(string fontReference) => null; }
public sealed record MediaRenderInfo(MediaAsset Asset, RenderSource Original, RenderSource? Proxy);
public sealed record RenderSource(string Path, long FormatStartMicros, FrameIndex? Index);
public static class TimelineCompiler { public static CompiledRender Compile(Project p, IMediaCatalog m, RenderRequest r); }
```

- **Determinism.** The compiler is pure, and the same inputs give byte-identical output.
- **Window.** `N = EndFrame - StartFrame` and `S = SamplesAt(EndFrame) - SamplesAt(StartFrame)`.
- **Preview scaling.** Pixel quantities (sizes, positions, font sizes, borders) are multiplied by `k = CanvasWidth / seq.Width`. Sizes round to even.
- **Fonts.** The compiler has no I/O, so the caller resolves title fonts first and answers `ResolveFont` with a bare name in the work directory. Without an answer, `builtin:Inter-*` maps to the bundled file name and anything else falls back to `Inter-Regular.ttf` with a warning.

### 7.2 Time and seek rules (video)

The frame shown at timeline frame `f` of clip `c` is the source frame with the largest pts `<= s(f)`, where `s(f) = SourceFlicksAt(c, f)` (sample-and-hold).

For a segment rendering `[g0, g1)`:
1. `s0 = origin + s(g0)`, where origin is the source `format.start_time`.
2. Pick the ordinal `i` and its time `P`:
   - With an index: `i` is the largest ordinal with `pts_i <= s0`, or 0.
   - CFR fallback: `i = max(0, floor((s0 - firstPts) * r))` and `P = firstPts + i/r`.
3. `seek = FloorMicros(pts_file(i)) - file.FormatStartMicros`, where `file` is the original or proxy actually used. Accurate input seek keeps the first frame with `pts >= seek`. **Never add a positive epsilon.**
4. `d0 = s0 - P`. It can be negative when the first frame is late.
5. Emit the setpts form with `speed = SpeedPercent/100`. Every emitted setpts is preceded by `settb=1/705600000` (the flick time base), and `d0` is written as an exact integer number of flicks. setpts truncates its result to the stream time base, so at Matroska's 1 ms a fractional `d0` or a speed division put frames into the wrong output slot; the rescale to flicks is exact for every common time base.
   - `d0 = 0` and speed 1: omit both.
   - `d0 = 0`: `settb=1/705600000,setpts=PTS/<speed>`.
   - Speed 1: `settb=1/705600000,setpts=PTS-<d0>`.
   - Otherwise: `settb=1/705600000,setpts=(PTS-<d0>)/<speed>`.
   - A negative `d0` is written `+<|d0|>`.
6. Then `fps=<R>:start_time=0:round=up`. `round=up` gives exact sample-and-hold, and `start_time=0` trims early frames or pads a late first frame.
7. **Never** use `PTS-STARTPTS` on a media input stream. It erases the source's audio-versus-video start offset.

**Input span** for `-t`: `n*speed/R + max(d0,0) + max(0.1 s, 2 source frames)`. Stills use `-loop 1 -framerate <R> -t <span> -i <file>` with no seek and no setpts.

**Verified:** frame exact in full and one-frame renders, for 25 fps sources on 30 and 30000/1001 timelines at 50%, 100% and 200% speed, with an off-grid sourceIn. Also for the 30000/1001 Matroska oracle (pts rounded to 1 ms) and the VFR oracle through their frame indexes, which needed the flick time base of step 5.

### 7.3 Segment chains

**Video media segment** (`n = g1 - g0` frames), in this order:
1. `[k:<videoStreamIndex>]`, then the setpts from section 7.2 if any.
2. `fps=<R>:start_time=0:round=up,tpad=stop_mode=clone:stop=<n>,trim=end_frame=<n>`.
3. `crop=w=iw*<1-l-r>:h=ih*<1-t-b>:x=iw*<l>:y=ih*<t>`, only if cropped. Fractions keep proxy and original consistent.
4. `scale=w=<SW>:h=<SH>:flags=<bicubic export|bilinear preview>:out_color_matrix=bt709:out_range=tv`.
5. `eq=` with only the non-neutral parameters, in the order brightness, contrast, saturation, gamma.
6. `setsar=1,format=yuva420p`, then `lut=a=val*<opacity>` if opacity < 1.
7. Fades in clip-extent time. When the segment offset `o > 0`, wrap them in `setpts=PTS-STARTPTS+<o>` ... `setpts=PTS-STARTPTS`; the timebase is exactly 1/R after `fps`. The fades are `fade=t=in:st=0.000000:d=<Fin/R>:alpha=1` and `fade=t=out:st=<(L-Fout)/R>:d=<Fout/R>:alpha=1`. User fades at an end that has a transition are ignored.
8. Canvas step (section 7.4).

**Geometry.** With source display size `dw x dh`, `fs = min(W/dw, H/dh)` and user scale `u`:
- `SW = even(dw*(1-l-r)*fs*u)` and `SH = even(dh*(1-t-b)*fs*u)`.
- `X = round(W/2 + x - dw*fs*u/2 + l*dw*fs*u)`, and `Y` likewise. Cropping never moves the remaining pixels.

**Title segment:** `color=c=black@0:s=<W>x<H>:r=<R>,trim=end_frame=<n>,format=yuva420p,drawtext=fontfile=<bare>:textfile=t_<sha8>.txt:expansion=none:fontsize=<px>:fontcolor=0xRRGGBB[@a]:borderw=<px>:bordercolor=0xRRGGBB@a[:box=1:boxcolor=..:boxborderw=<px>]:x=<expr>:y=<expr>`, then opacity and fades as for media.
- **Anchors:** Center is `(w-text_w)/2`, `(h-text_h)/2`. LowerThird is `(w-text_w)/2`, `h*0.8-text_h/2`. Top is `(w-text_w)/2`, `h*0.1`. Offsets append `+<px>`.
- `expansion=none` is mandatory.

**Audio segment** (`S_seg = SamplesAt(g1) - SamplesAt(g0)`, seek `FloorMicros(s(g0))`):
1. `[k:<audioStreamIndex>]aresample=48000:async=1:first_pts=0` (pads leading silence when the audio starts after the seek point).
2. Only if speed != 1, the atempo pre-roll (`AtempoPreRoll`): the seek moves 0.2 s (9600 source samples) earlier, and `adelay=<d>S:all=1` pads with silence the part of those 0.2 s that lies before the file start. Then the `atempo` steps within [0.5, 2.0], then `atrim=start_sample=<H>,asetpts=PTS-STARTPTS`, where `H = round(9600*100/speed) - lead(speed)`. atempo (WSOLA) fades in its first window and puts its output early by a speed-dependent lead (about 4.5 to 32 ms); the lead is calibrated every 5 percent per chain shape (0.5 then f, f alone, 2 then f) on 100 ms Hann-windowed 997 Hz tone bursts and interpolated linearly. It depends a little on the content (a 311 Hz burst differs by up to 2 ms at 25 percent, under 0.5 ms from 50 percent up). Every segment and every playback window therefore starts in atempo's steady state, so window seams neither skip nor repeat source audio. A single isolated click cannot be placed better than about half an atempo window (WSOLA moves it with its phase), so the calibration and its integration test (`AtempoAlignmentTests`: within 1 ms at 25, 50, 150, 200 and 300 percent) use bursts.
3. `aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo`, then `apad=whole_len=<S_seg>,atrim=end_sample=<S_seg>`.
4. `asetpts=PTS-STARTPTS+<oS>`, only when `oS > 0`. The timebase is exactly 1/48000.
5. `volume=<g>dB`, or the envelope from section 7.7.
6. `afade`, `curve=tri`, for the crossfade or the user fade at each end, with `ss` and `ns` in samples from the extent start.
7. `asetpts=PTS-STARTPTS,adelay=<SamplesAt(g0)-SamplesAt(f0)>S:all=1`.

`first_pts` is verified: audio that starts 0.5 s late kept its impulse at sample 24000, where `asetpts=PTS-STARTPTS` moved it to sample 0.

### 7.4 Runs and compositing (one model)

**Base.** `color=c=black:s=<W>x<H>:r=<R>,trim=end_frame=<N>,format=yuv420p[base]`.

**Runs.** Each visible video track, bottom-up, is split into **runs**: maximal butting sequences of enabled clips. Only runs that intersect the window are compiled. Inside a run:
- Plain cuts use `concat=n=<k>:v=1:a=0,settb=<1/R>` (for example `settb=1/30` or `settb=1001/30000`). concat always outputs in the 1/1000000 time base, which would turn the frame offset of the placement `setpts` into microseconds and make a following xfade reject its inputs; settb rounds each timestamp back to its exact frame. Pending cut segments are concatenated before each xfade.
- A transition uses `xfade=transition=<kind>:duration=<d/R>:offset=<(Lacc-d)/R>`, where `Lacc` is the accumulated frame count.
- The kinds are fade, fadeblack, fadewhite, wipeleft, wiperight, slideleft, slideright and circleopen.
- Every segment entering a join is canvas sized, yuva420p, SAR 1 and timebase 1/R. xfade accepts only 4:4:4 formats, so ffmpeg converts every frame of a run with a transition to yuva444p on the way to the xfade (and back to yuva420p at the composite overlay).

**Canvas step.**
- **Runs of several clips** (decided on the whole run, not on the window):
  - Identity at canvas size: nothing.
  - Identity with a smaller fit: `pad=w=<W>:h=<H>:x=<px>:y=<py>:color=black@0` (safe, because fit <= canvas).
  - Transformed: `color=c=black@0:s=<W>x<H>:r=<R>,trim=end_frame=<n>,format=yuva420p[cv..];[cv..][seg]overlay=x=<X>:y=<Y>:format=yuv420:eof_action=pass:threads=1`. Negative and oversized positions work. **Never place with `pad`**, which fails when the clip exceeds the canvas. `threads=1` is required: overlay's slice-threaded blend onto a main input with alpha races when the overlaid pixels are partly transparent (opacity, fades, source alpha), and identical renders differed run to run. The composite overlays onto the opaque base are deterministic and keep their threads.
- **Runs of a single clip** skip the canvas and overlay directly at `(X, Y)`. An identity fit uses `X = (W-SW)/2`.
- The transformed canvas is written `<segment chain>,[cv<t>s<n>]overlay=...[v<t>s<n>]`, appended after the segment's last filter (`format=yuva420p`, the opacity `lut` or the fades); labelled pads are linked first, so the canvas is the main input. The `color=...[cv<t>s<n>]` chain follows on the next line.

**Placement.**
- A run ends with `setpts=PTS-STARTPTS+<k>` (frames), where `k = max(u0, f0) - f0`, and is labelled `[v<t>r<m>]`.
- It is stacked with `[prev][v<t>r<m>]overlay=x=<X>:y=<Y>:format=yuv420:eof_action=pass[c<i>]`.
- Overlay ignores a run before its first frame and passes the base through after its end. Gaps and fades to black need no generators, and a source that ends early only shortens its own run.

### 7.5 Range windows

Every range compiles through one path: export, one-frame scrub, playback window, In/Out.
- **Widening.** A run's interval `[u0, u1)` is its intersection with the window, widened outward when an end falls inside one of its transition regions.
- **Trimming back.** `trim=start_frame=<A>:end_frame=<B>` after the join cuts it back.
- **Same pixel path in every window.** A frame must take the same filters whatever the window, so the canvas step depends on the whole run: a run of several clips takes it for every segment even when the window needs only one, and the run is overlaid at (0, 0). A run with a transition whose window needs no xfade appends `format=yuva444p` after its joins, reproducing the conversion xfade forces on the full render.
- **Verified.** A one-frame render of a mid-crossfade frame was byte-identical to that frame of the full render. Also one-frame windows on every side of a crossfade, inside title fades and on a transformed, cropped, 80% opaque picture-in-picture clip that butts against a title, at 30 and 30000/1001, short windows across a wipe, and one-frame windows in runs where a cut follows or precedes a transition, whose `concat` joins yuva444p and yuva420p segments (`WindowEqualityTests`).

### 7.6 Audio tracks and outputs

- **Per track.** Segments, including crossfade pairs, go through `amix=inputs=<n>:duration=longest:dropout_transition=0:normalize=0,apad=whole_len=<S>,atrim=end_sample=<S>` to give `[a<t>]`. A single segment skips amix. Overlapping linear fades sum to constant gain, so no `acrossfade` is needed.
- **Mixed (export):**
  - Included tracks are those not muted and, when anything is soloed, soloed.
  - Each gets `volume=<g>dB` when its gain is not 0.
  - The tracks are combined with `amix` (skipped for one), then `alimiter=limit=0.98:level=0:latency=1` when Limit peaks is on. `level` defaults to true, which rescales the output, and `latency` defaults to false, which delays it, so both must be set.
  - `apad`/`atrim` to S finishes it as `[aout]`.
  - Without audio clips the output is `anullsrc=r=48000:cl=stereo,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,atrim=end_sample=<S>[aout]`.
- **PerTrack (preview):**
  - Every track is present, with empty tracks as `anullsrc`. Mute and gain are not applied in the graph.
  - `[a0]...[aN-1]amerge=inputs=N[aout]`, or `anull` for N = 1.
  - Output is `-f s16le -c:a pcm_s16le` with 2N channels (verified for N = 2, 5, 8).
  - If it fails at runtime, playback falls back to Mixed, where mute and solo changes restart playback.
- **Speed in windows.** Both layouts carry the atempo pre-roll of 7.3, so a playback window that starts inside a speed-changed clip begins in atempo's steady state and its first sample follows the previous window's last one (`AtempoAlignmentTests` joins 37-frame windows at 50 and 200 percent).

### 7.7 Tails, numbers, labels, delivery

**Video tails:**
- Export: `setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv,format=<preset pix_fmt>[vout]`.
- Preview: `[framestep=step=<m>,]setparams=...,format=bgra[vout]`.
- GIF: `setparams=...,fps=15,split[g0][g1];[g0]palettegen=stats_mode=diff[p];[g1][p]paletteuse=dither=bayer:bayer_scale=4[vout]`.
- With no runs, the base chain continues straight into the tail.

**Envelope.** Keys `(t_i, g_i)` are seconds from the extent start, with clip gain folded in. The filter is `volume=volume='pow(10,(<inner>)/20)':eval=frame`, where `inner` nests `if(lt(t,<t_{i+1}>),<g_i>+(<g_{i+1}-g_i>)*(t-<t_i>)/<t_{i+1}-t_i>, ...)` and ends with the last value. It gets the prefix `if(lt(t,<t_0>),<g_0>,` when `t_0 > 0`.

**`FfNum`** is the only number formatter and uses invariant culture throughout:
- seconds: 6 decimals, floored to the microsecond
- decimals: `F6`
- colour alpha: up to 3 decimals, trimmed
- rates: `30` or `30000/1001`
- frames and pixels: integers

**Labels**, in emission order:
- `[base]`
- per video track `t`, bottom-up: `[v<t>s<n>]`, canvases `[cv<t>s<n>]`, joins `[v<t>j<x>]`, xfades `[v<t>x<y>]`, runs `[v<t>r<m>]`, composites `[c<i>]`
- per audio track, top-down: `[a<t>s<n>]`, then `[a<t>]`
- `[vout]`, `[aout]`

Single-segment runs and tracks continue in the same chain. Inputs are numbered by first use. Two uses share one input only when they cannot desynchronise: equal path, seek and still flag, and segments that start at the same timeline frame at the same speed (the linked audio and video of a clip, or one clip stacked on two tracks). The shared input keeps the larger span. Every other use gets its own `-i`: one input feeding segments far apart on the timeline (a duplicated clip) made ffmpeg buffer decoded frames for the later use without bound (4.5 GB against 0.3 GB working set in the review experiment).

**Delivery.**
- Exports write `g_<sha12>.ffg`: UTF-8 without BOM, `\n` line endings, one chain per line.
- Previews pass the graph inline with `-filter_complex` when it is at most 8 KiB, otherwise as a script.
- Script option: Slash uses `-/filter_complex <file>` and LegacyScript uses `-filter_complex_script <file>`. InlineOnly always passes the graph inline and fails clearly above 30,000 characters.

### 7.8 Worked example (verified)

**Timeline.** 1920x1080 at 30 fps.
- V1: A (`a.mp4`, sourceIn 2.0 s) on frames 0 to 150 and B (`b.mp4`, sourceIn 10.0 s, saturation 1.2) on frames 150 to 270, with a 30-frame crossfade.
- V2: the title "Day One" (Inter Bold 96, outline 3, lower third, 15-frame fades) on 30 to 120, and C (`c.mp4`, sourceIn 0) on 180 to 270 as PiP (scale 0.4, offset +480/-270).
- A1: linked A and B audio with a 30-frame crossfade.
- A2: C audio with keys at 0 (-20 dB) and frame 30 (0 dB).

`g_8bad29d60e0e.ffg`:
```
color=c=black:s=1920x1080:r=30,trim=end_frame=270,format=yuv420p[base];
[0:0]fps=30:start_time=0:round=up,tpad=stop_mode=clone:stop=165,trim=end_frame=165,scale=w=1920:h=1080:flags=bicubic:out_color_matrix=bt709:out_range=tv,setsar=1,format=yuva420p[v0s0];
[1:0]fps=30:start_time=0:round=up,tpad=stop_mode=clone:stop=135,trim=end_frame=135,scale=w=1920:h=1080:flags=bicubic:out_color_matrix=bt709:out_range=tv,eq=saturation=1.200000,setsar=1,format=yuva420p[v0s1];
[v0s0][v0s1]xfade=transition=fade:duration=1.000000:offset=4.500000,setpts=PTS-STARTPTS+0[v0r0];
[base][v0r0]overlay=x=0:y=0:format=yuv420:eof_action=pass[c0];
color=c=black@0:s=1920x1080:r=30,trim=end_frame=90,format=yuva420p,drawtext=fontfile=Inter-Bold.ttf:textfile=t_58fdd4da.txt:expansion=none:fontsize=96:fontcolor=0xFFFFFF:borderw=3:bordercolor=0x000000@0.6:x=(w-text_w)/2:y=h*0.8-text_h/2,fade=t=in:st=0.000000:d=0.500000:alpha=1,fade=t=out:st=2.500000:d=0.500000:alpha=1,setpts=PTS-STARTPTS+30[v1r0];
[c0][v1r0]overlay=x=0:y=0:format=yuv420:eof_action=pass[c1];
[2:0]fps=30:start_time=0:round=up,tpad=stop_mode=clone:stop=90,trim=end_frame=90,scale=w=768:h=432:flags=bicubic:out_color_matrix=bt709:out_range=tv,setsar=1,format=yuva420p,setpts=PTS-STARTPTS+180[v1r1];
[c1][v1r1]overlay=x=1056:y=54:format=yuv420:eof_action=pass,setparams=colorspace=bt709:color_primaries=bt709:color_trc=bt709:range=tv,format=yuv420p[vout];
[0:1]aresample=48000:async=1:first_pts=0,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_len=264000,atrim=end_sample=264000,afade=t=out:ss=216000:ns=48000:curve=tri,asetpts=PTS-STARTPTS,adelay=0S:all=1[a0s0];
[1:1]aresample=48000:async=1:first_pts=0,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_len=216000,atrim=end_sample=216000,afade=t=in:ss=0:ns=48000:curve=tri,asetpts=PTS-STARTPTS,adelay=216000S:all=1[a0s1];
[a0s0][a0s1]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0,apad=whole_len=432000,atrim=end_sample=432000[a0];
[2:1]aresample=48000:async=1:first_pts=0,aformat=sample_fmts=fltp:sample_rates=48000:channel_layouts=stereo,apad=whole_len=144000,atrim=end_sample=144000,volume=volume='pow(10,(if(lt(t,1.000000),-20.000000+(20.000000)*(t-0.000000)/1.000000,0.000000))/20)':eval=frame,asetpts=PTS-STARTPTS,adelay=288000S:all=1,apad=whole_len=432000,atrim=end_sample=432000[a1];
[a0][a1]amix=inputs=2:duration=longest:dropout_transition=0:normalize=0,alimiter=limit=0.98:level=0:latency=1,apad=whole_len=432000,atrim=end_sample=432000[aout]
```

```
ffmpeg -hide_banner -nostats -progress pipe:1 -v warning -y
  -ss 2.000000 -t 5.600000 -i "D:/Clips/a.mp4"  -ss 9.500000 -t 4.600000 -i "D:/Clips/b.mp4"
  -ss 0.000000 -t 3.100000 -i "D:/Clips/c.mp4"  -/filter_complex g_8bad29d60e0e.ffg
  -map "[vout]" -map "[aout]" -c:v libx264 -preset medium -crf 20 -pix_fmt yuv420p -profile:v high
  -fps_mode cfr -r 30 -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv
  -c:a aac -b:a 192k -ar 48000 -ac 2 -movflags +faststart "D:/Out/final.partial.mp4"
```

**Checks:**
- Video: 165 + 135 - 30 = 270 frames.
- Audio: 264000 + 216000 - 48000 = 432000 samples, which is 9.000 s.
- PiP: SW = 768 and X = 960 + 480 - 384 = 1056.
- ffprobe counted 270 frames and reported BT.709 tags.
- Names: `t_58fdd4da.txt` and `g_8bad29d60e0e.ffg` are the real SHA-256 content addresses of the title text and of this script. `WorkedExampleTests` compiles the timeline and compares the script and the command above byte for byte.

**Scrub version.** Frame 150 at Half quality compiles to `-ss 6.500000 -t 1.100000` and `-ss 9.500000 -t 1.100000` inputs, 30-frame segments at 960x540, and `xfade ... offset=0.000000,trim=start_frame=15:end_frame=16`. With `-map [vout] -frames:v 1 -fps_mode passthrough -f rawvideo -pix_fmt bgra pipe:1` it returns exactly 960*540*4 bytes.

---

## 8. Preview engine

### 8.1 Frame server (scrub and step)

```csharp
public sealed record PreviewSize(int Width, int Height);                                   // even
public sealed record FrameRequest(Project Snapshot, long Frame, PreviewSize Size, bool UseProxies, long RequestId);
public sealed class VideoFrame { public byte[] Buffer; public int Width, Height, Stride; public long TimelineFrame, RequestId; public bool Pooled; }
public interface IVideoFrameSink { void Present(VideoFrame frame); }                       // implemented in App
public sealed class FrameServer : IAsyncDisposable
{ public void Request(FrameRequest r); public event Action<long, string>? RenderFailed; }  // latest wins
```

**Protocol:**
1. `Request` writes to a channel bounded to 1 with `DropOldest`. The single worker reads it.
2. The worker compiles `[f, f+1)` as PreviewFrame and computes the cache key. A hit is presented immediately.
3. On a miss it writes the aux files, runs `ffmpeg -hide_banner -nostdin -v error <inputs> -filter_complex <graph> -map [vout] -frames:v 1 -fps_mode passthrough -f rawvideo -pix_fmt bgra pipe:1`, reads exactly `W*H*4` bytes with `Stream.ReadExactly`, caches them and presents them.
4. A running scrub process is **never** killed. Its frame is close to the target, and killing it would leave the user with no feedback.
5. **Prefetch.** After 150 ms idle while paused, render `[f-6, f+18)` as one PreviewVideo window and cache each frame under its own single-frame key (compiled as text only). A new request kills a prefetch, never a scrub.
6. **Placeholder.** On every playhead change the viewer shows the nearest thumbnail of the topmost visible clip, dimmed, until a frame with the latest `RequestId` arrives. It appears only after 120 ms without that frame, so cache hits and quick scrubs never flash it, and never while playing.

**`FrameCache`** is a 512 MB LRU keyed by the SHA-256 of the graph text, the input arguments, each input's fingerprint plus whether it is the proxy, and the preview size. Unrelated edits and undo keep their hits. Cached frames own their arrays and are never mutated.

**Cache and prefetch (M5).** `FrameCache` is one instance shared by the frame server (the constructor overload that takes it) and `PlaybackSession`; the old constructor creates a private one. Prefetch runs only while `FrameServer.PrefetchEnabled` is true: the viewer turns it on while paused and off while playing, because the server cannot tell. Prefetch frames are read into owned arrays and cached only when their key is missing; a new request (or dispose) kills the prefetch process.

**Frame server rules (M4).** Presented misses are pooled buffers; hits are the cached array, unpooled, so sinks must never write into a frame. A negative frame renders frame 0, a scrub that delivers nothing within 30 s is killed and reported, and `RenderFailed` carries every other failure (odd size, compile error, ffmpeg error). `MediaCatalog` answers null for a file that is missing at registration or fails its background re-probe, so the compiler treats it as offline; an asset registered without an index (an opened project) is probed and indexed in the background, two at a time, and raises `IndexReady` so the viewer can request the frame again with exact seeks. `MediaImporter` builds the index before it registers the asset.

### 8.2 Playback

```csharp
public sealed record PlaybackOptions(Project Snapshot, long StartFrame, long EndFrame, int Speed, PreviewSize Size,
    bool UseProxies, MixerState Mixer)
{ public bool Loop { get; init; } public long? LoopStartFrame { get; init; }
  public double WindowSeconds { get; init; } = 10; public double PrefetchSeconds { get; init; } = 3; }
public sealed record MixerState(ImmutableArray<MixerTrack> Tracks, double Master);       // linear master
public sealed record MixerTrack(string TrackId, bool Muted, bool Solo, double GainDb);
public sealed class PlaybackSession : IAsyncDisposable                                   // one per viewer, reused
{
    public PlaybackSession(ProcessRunner runner, FfmpegToolset toolset, FfmpegCapabilities caps, WorkDirectory work,
        IMediaCatalog catalog, FontLocator fonts, IAudioOutput audio, FrameCache cache);
    public void Start(PlaybackOptions o); public void Start(long fromFrame, int speed);   // UI thread, never blocks
    public long Stop(); public Task<long> StopAsync();                                   // returns the frame to park on
    public long Position { get; } public long ClockFrame { get; }                        // the same clock frame
    public bool TryTakeFrame(out VideoFrame? frame);                                     // target = Position
    public bool TryTakeFrame(long target, out VideoFrame? frame, out int dropped);       // UI thread
    public void SetMixer(MixerState state);                                              // live, no restart
    public MixerPeaks ReadAndResetPeaks(); public PlaybackStats Stats { get; } public bool IsPlaying { get; }
    public event Action<long>? PositionChanged; public event Action? ReachedEnd; public event Action<string>? Failed;
}
public interface IAudioOutput : IDisposable   // OpenAlAudioOutput, NullAudioOutput (Audio/)
{ void Open(int sampleRate, int channels); bool Enqueue(ReadOnlySpan<short> samples); long PlayedSamples { get; }
  int FreeBuffers { get; } void Start(); void Pause(); void Stop(); void Flush(); bool IsAvailable { get; } }
```

- **Session lifetime.** The session is created once and started many times (the static `StartAsync` of earlier drafts became `Start`, so shuttle and loop restarts reuse it). Each start is an internal run; startup and teardown run in the background, and a run waits for the previous run's teardown before it touches the audio output. `Stop()` returns at once with the parked frame (the last frame handed to the presenter, else the start frame); `StopAsync` also waits until every process is gone. Events are raised on worker threads.

**Windows.**
- A window covers `10 s * Speed` of timeline frames.
- Each window is one video process (PreviewVideo, `FrameStep = Speed`, rawvideo bgra on stdout) and, at speed 1 only, one audio process (PreviewAudio, PerTrack).
- Video output frame `k` is timeline frame `start + k*Speed`, and a window outputs `ceil(n/Speed)` frames.
- The next window's processes start when the reader has 3 s left. Their output is read after EOF of the current window.
- Window audio is `SamplesAt(f1) - SamplesAt(f0)` samples, so seams are gapless and frame exact.
- A window that starts inside a clip seeks its audio like a scrub. In a Matroska source the packet timestamps are rounded to 1 ms, so the content after such a seam can sit up to 48 samples off (measured: 16 samples in `sync.mkv` at 4.0 s, where the impulse on the seam itself was trimmed away). Sample counts stay exact, so this never accumulates; mp4, mov and wav sources are sample exact. The playback tests therefore keep seams off the impulses and allow 1 ms.
- Each AL buffer holds one mixed block of 1024 frames; only the last block of a run (or the block before a Mixed fallback) is shorter, and `unqueuedFrames` adds each buffer's real length.
- `Open` (every run) stops the source, detaches its queue and rewinds it to `AL_INITIAL`. A source left `AL_STOPPED` reports every newly queued buffer as processed, so the second run of a session counted unplayed buffers and its clock raced to the end (found by the M5 smoke run).

**Readers and audio output.**
- **VideoWindowReader** fills a queue of 8 pooled frames. While the queue is full it stops reading, and the pipe throttles ffmpeg (no `-re`).
- **AudioFeeder**, every 5 ms:
  1. Read PCM into a ring buffer.
  2. Unqueue processed AL buffers and add 1024 frames to `unqueuedFrames` for each.
  3. Mix 1024-frame blocks to stereo and requeue them, keeping 6 buffers (128 ms) queued.
  4. If the source stopped while buffers are still queued (an underrun), restart it and count the underrun.
- **Start.** `SourcePlay` waits for the first video frame and 6 filled buffers. The cached frame at `StartFrame` is shown at once.

**Clock.**
- Audio is the master. The feeder publishes `clockSamples = SamplesAt(StartFrame) + unqueuedFrames + AL_SAMPLE_OFFSET` with a Stopwatch timestamp. `AL_SAMPLE_OFFSET` counts from the first buffer still queued, which is why only unqueued buffers are summed.
- Readers extrapolate from the last publish by at most 20 ms.
- `ClockFrame = FrameAtSample(clockSamples)`.
- At speeds 2 and 4, or with `NullAudioOutput`, a Stopwatch drives `start + floor(elapsed*R*Speed)`. `NullAudioOutput.PlayedSamples` is itself `floor(elapsed*48000)`, so at speed 1 the feeder publishes it like a device and the result is the same Stopwatch clock. A device lost during playback switches to the same Stopwatch continuation (5.6, `AudioClockSource`).
- The feeder never queues an empty PCM block: the PerTrack to Mixed fallback can leave one behind, and OpenAL rejects an empty buffer, which ended playback.
- The clock never passes `EndFrame` and never steps back (an extrapolation that overshoots a stalled publish holds).

**Presenter.** Each animation frame calls `TryTakeFrame(ClockFrame)`. Older frames are dropped and counted, and the newest frame at or before the target is copied (section 10.3). An underrun holds the last frame. After 2 s of continuous drops the viewer shows a badge.

**Edits and the end.**
- Timeline changes dispose the session and restart it at `ClockFrame` after 150 ms of quiet. Mixer changes call `SetMixer`.
- At `EndFrame`, a loop restarts at In, otherwise playback stops.

**`AudioMixer.Mix(ReadOnlySpan<short> in2N, Span<short> stereo, int frames)`:**
- A track is active when it is not muted and either nothing is soloed or it is soloed.
- `out = clamp(round(sum(active_t * in_t * 10^(gainDb_t/20)) * master))`.
- `ReadAndResetPeaks()` returns per-track and master peaks.

**Reverse (M8).** `ReverseSession` renders 1 s windows `[f-R, f)` into memory and presents them backward on a Stopwatch clock, silently, while the previous window renders.

---

## 9. Export

**`ExportService.Enqueue(ExportRequest)`** returns an `ExportJob` (state, fraction, ETA, speed, error, `Cancel()`). Jobs run one at a time.

**Flow:**
1. Copy the bundled fonts and resolve every enabled title's font with `FontLocator` (user fonts become `font_<sha8>.<ext>`), handing the bare names to the compiler through `IMediaCatalog.ResolveFont`. Every title counts, not only those in the range, because widening across a transition (7.5) pulls in a neighbour; warnings name only the fonts inside the range. Compile with `Purpose=Export`. A graph with `drawtext` on a build without it fails clearly.
2. Write the aux files and the script.
3. Run ffmpeg with stdin redirected, writing to `<dir>/<name>.partial.<ext>`. When a file of that name already exists (somebody else's, or a crashed export's), write `<name>.partial-<8 hex>.<ext>` instead: an export never deletes a file it did not write. The output and the partial file are compared with every project media file by file identity (`FileIdentity`: volume serial and file index on Windows, `st_dev` and `st_ino` on Linux), not by spelling, and a clash fails the job before ffmpeg starts.
4. Parse progress.
5. On exit 0 with `progress=end`, call `File.Move(partial, final, overwrite: true)`, retried 10 times 200 ms apart. When the target stays locked (open in a player), the finished file is renamed to the first free `<name> (n).<ext>`, which becomes the job's `OutputPath` with a warning; as a last resort it stays as the partial file. A completed partial file is never deleted.

**`ProgressParser`:**
- Splits `key=value` lines and **trims** values (`speed= 5.7x` has a leading space).
- A block ends at `progress=continue` or `progress=end`.
- Reads `out_time_us` (or `out_time_ms`, which is also microseconds), skipping `N/A`.
- `fraction = out_time_us / (N/R*1e6)` and `ETA = remaining / speed`.
- UI updates are throttled to 10 Hz.

**Cancel and failure.**
- **Cancel:** write `q`, wait 3 s, kill the tree, delete the partial file.
- **Failure:** delete the partial file and raise `FfmpegException(exitCode, stderrTail(30), displayCommand)`.

**Common output arguments.**
- Video: `-fps_mode cfr -r <R> -colorspace bt709 -color_primaries bt709 -color_trc bt709 -color_range tv`, except for GIF and audio-only.
- Audio: `-ar 48000 -ac 2`.
- Audio-only presets compile with `IncludeVideo=false`, and GIF with `IncludeAudio=false`.

| Preset | Arguments | Quality D/G/H/B |
|---|---|---|
| H.264 | `-c:v libx264 -preset medium -crf {q} -pix_fmt yuv420p -profile:v high -c:a aac -b:a 192k -movflags +faststart` (fallback `-c:v libopenh264 -b:v {4/8/12/20 Mb/s scaled by pixels/1080p}`) | 28/23/20/17 |
| H.265 | `-c:v libx265 -preset medium -crf {q} -pix_fmt yuv420p -tag:v hvc1 -c:a aac -b:a 192k -movflags +faststart` | 30/26/23/20 |
| VP9 | `-c:v libvpx-vp9 -crf {q} -b:v 0 -row-mt 1 -deadline good -cpu-used 2 -pix_fmt yuv420p -c:a libopus -b:a 160k` (fallback `-c:a libvorbis -q:a 5`) | 40/34/31/26 |
| AV1 | `-c:v libsvtav1 -preset 8 -crf {q} -pix_fmt yuv420p` (fallback `-c:v libaom-av1 -cpu-used 6 -row-mt 1 -crf {q} -b:v 0`) `-c:a aac -b:a 192k -movflags +faststart` | 42/36/32/28 |
| ProRes 422 HQ | `-c:v prores_ks -profile:v 3 -vendor apl0 -pix_fmt yuv422p10le -c:a pcm_s16le` | fixed |
| GIF | GIF tail (7.7), `-loop 0`, canvas = chosen width | fixed |
| MP3 | `-vn -c:a libmp3lame -q:a {q}` | 6/4/2/0 |
| AAC / WAV / FLAC | `-vn -c:a aac -b:a 256k` (.m4a) / `-vn -c:a pcm_s16le` / `-vn -c:a flac` | fixed |

- **Graph tail.** The video tail's `format=` matches the preset `-pix_fmt`.
- **Availability.** A preset is greyed out when none of its encoders is in the capability set.
- **Many inputs.** More than 64 inputs shows a memory warning and proceeds. M8 adds segmented export:
  1. Split at frames outside all transition regions into windows of at most 48 inputs.
  2. Render each window to FFV1 + PCM MKV.
  3. Join them with the concat demuxer and encode once.
- **Every preset (M7).** `build/smoke/m7.smoke` exports a sequence with an `xfade` crossfade and a `drawtext` title once per preset through the export dialog and checks codec, container, frame count (GIF: 15 fps) or audio duration, `hvc1` and the BT.709 tags with ffprobe. All ten passed on N-126308 with the flags above unchanged.
- **Command preview.** The dialog's "Show ffmpeg command" is `ExportCommandPreview.BuildAsync`: step 1 of the flow (`ExportFontResolver`, shared with `ExportService`), the same compile, the same `g_<sha12>.ffg` name and arguments, so it equals the job's `DisplayCommand`.
- **Hardware encoders (M8).** Each is enabled after a test encode such as `-f lavfi -i color=s=256x256:r=30:d=0.2 -c:v h264_nvenc -f null -` passes:
  - NVENC: `-preset p5 -rc vbr -cq {q} -b:v 0`
  - AMF: `-quality quality -rc cqp -qp_i {q} -qp_p {q}`
  - QSV: `-global_quality {q}`

---

## 10. Cutlass.App (MVVM)

### 10.1 Composition and view models

**Composition.**
- `Program`: `AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace()`.
- `App.axaml`: `RequestedThemeVariant="Dark"`, `<FluentTheme/>` and `Themes/CutlassTheme.axaml`.
- `OnFrameworkInitializationCompleted` builds `AppServices` by hand (no DI container) and creates `MainWindowViewModel`. `AppServices` holds settings, paths, toolset, work directory, runner, registry, catalog, scheduler, frame cache, frame server, export service, audio output and dialog service.

**View model rules (CommunityToolkit.Mvvm 8.4.2):**
- Partial properties: `[ObservableProperty] public partial bool IsPlaying { get; set; }`.
- `[RelayCommand]` for commands.
- Every view and DataTemplate sets `x:DataType`.
- View models never touch controls.

**View models:**
- `MainWindowViewModel`: the session, file commands, child view models.
- `MediaBinViewModel` and `MediaItemViewModel`.
- `ViewerViewModel`: mode, quality, timecode (click to type through `TimecodeEntry`), placeholder, two-up hints.
- `SourceViewerViewModel` (M6): the Source monitor. It holds the opened media as a private one-clip sequence (video on V1, audio on A1, the media's display size and nearest supported rate) in its own `ProjectSession` and `TimelineViewModel`, so the frame server renders it like any snapshot and a second `PlaybackController` plays it on the shared `PlaybackSession`; only one monitor plays at a time. Source frame `f` is `f * FlicksPerFrame` flicks of source time, so `,` and `.` hand `TimelineViewModel.SourceInsert/SourceOverwrite` the In and Out in flicks.
- Source drags (M6, DESIGN 4.3): `ViewerViewModel.CreateSourceDrag` builds a `SourceDrag` (media id, Source In and Out in flicks, `SourceStreams` Both, Video or Audio), which `ViewerView` publishes as invariant text under `DragFormats.SourceRange` ("cutlass/source-range") from the canvas (both streams) and the "Video only" and "Audio only" grips. `TimelineControl` parses it and calls `TimelineViewModel.PreviewSourcePlacements` for the ghost and `PlaceSource` on drop. Both use `TimelineEdits.SourceRangeFrames` for the length and `TimelineEdits.SourcePlacements` with a null track for the stream that is left out; a single-stream drop on a track of the other kind goes to the matching track of the dragged kind (same index, else V1 or A1).
- Effect drops (M6, DESIGN 4.2): the shell sets `TimelineViewModel.Effects` to the `EffectsViewModel`. `TimelineControl` accepts `EffectItem.DragFormat` ("cutlass/effect-id") only over a track row, asks `EffectsViewModel.PreviewAt` for the ghost (an Accent band on the nearest cut of the matching kind, a TitleClip ghost for a title, an Accent outline on the clip a fade preset reaches) and calls `ApplyAt` on drop. Fade presets also reach linked partners while linked selection is on.
- The default transition (double-click in the Effects tab) is saved in `AppSettings.DefaultTransition` (a `TransitionKind` name; unknown names and AudioCrossfade fall back to Crossfade) whenever it changes, and restored at startup.
- `TimelineViewModel`: `TimelineViewState`, the selection, and all timeline commands. No per-clip view models.
- `InspectorViewModel` and its sections. Setters use `Execute(..., coalesceKey)`, and scrubby drags use gestures.
- `ExportDialogViewModel`, `JobsViewModel`, `SettingsViewModel`, `WelcomeViewModel`.
- `SettingsViewModel` (M7, DESIGN 10) edits a working copy of `AppSettings` and talks to the app through `ISettingsHost`, which `AppServices` implements: `CheckFfmpegAsync` (`FfmpegPathCheck` file rules, `FfmpegToolset.ValidateAsync`, then `CapabilityProbe` with `forceRefresh`), `MeasureCacheAsync`, `ClearCacheAsync`, `ListAudioDevicesAsync` (`OpenAlAudioOutput.ListDevices`), all off the UI thread, and `ApplySettingsAsync`. That saves through `SettingsStore` (temp file, flush, rename), sets `MediaCache.LimitBytes` (an LRU pass when it shrank) and `MediaPipeline.ProxyPolicy`, and raises `AppServices.SettingsChanged(old, new)` on the caller's (UI) thread; `MainWindowViewModel` applies the viewer defaults, default durations (`TimelineViewModel.StillDurationSeconds` and `ConfirmTrackDeletes`, `EffectsViewModel.TransitionLengthSeconds` and `TitleLengthSeconds`), the autosave interval and the proxy re-queue from it.

**Offline media and relink (M7, DESIGN 11).** `MediaBinViewModel` holds the link state of each media. The shell sets `TimelineViewModel.IsMediaOffline` to `MediaBinViewModel.IsMediaOffline` and forwards `MediaBin.LinkStatesChanged` to `TimelineViewModel.NotifyMediaLinkStatesChanged`, whose `MediaLinkStatesChanged` makes `TimelineControl` redraw; a clip is drawn offline (red stripes and the `OFFLINE` badge) when its media is missing from the project or its file is missing. File > Relink media... runs `CommandIds.RelinkMedia` (no shortcut) through the command registry. The open scan's offline ids (including `RelinkScan.UnconfirmedIds`) become the shell's untouched set before the session is replaced: `MediaCatalog.KnownOffline` registers them offline without `File.Exists`, `MediaBinViewModel.SkipFileCheck` keeps them offline without a file check or poster, and the media jobs skip them, until `ApplyRelink` releases them (it also drops their catalog entries so an unchanged confirmed asset registers online). "Confirm location" in the Relink dialog calls `MediaRelinker.ScanAsync(project, projectPath, confirmedIds)` and turns a found file into the row's new path. Autosave also starts when `AppServices.StartAsync` throws (an unusable data or work folder), since the project can still be edited structurally; a missing ffmpeg is a normal startup result and starts it as usual.

**Project replacement and running work.** `TimelineViewModel.ProjectGeneration` counts `ChangeKind.Replace` (New, Open, Recover). On a replace `MediaBinViewModel` cancels the token of running imports (`ImportFile`, normally `MediaImporter.ImportAsync`), removes their placeholders and returns no assets, so a slow import never adds media to the project that became current. The welcome drop, the timeline file drop and "Import media..." also compare the generation after their awaits before they place anything.

**Autosave owners and crash paths (DESIGN 11).** Every autosave write (timed, after a failure, or the crash write) first writes `<autosave>.lock` with the process id and start time (`AutosaveRecovery.CurrentOwner`); deleting the autosave deletes the lock. `AutosaveRecovery.FindUntitled`, `ForProject` and `FindAll` skip autosaves whose owner is alive (`IsProcessAlive` compares the start time, so a reused process id counts as dead), so a second instance never offers, recovers into or discards another instance's live autosave. `ProjectStore` writes each atomic save through a unique temp file (`<path>.<pid>.<8 hex>.tmp`). An unobserved task exception (the app keeps running) autosaves through the normal serialised `AutosaveService` path, so a clean close deletes it; only a terminating exception and, on Linux and other POSIX systems, SIGTERM, SIGHUP and SIGINT (`PosixSignalRegistration`, not registered on Windows) use the bounded blocking write (`TrySaveNowBlocking`, 5 s, also serialised and tracked) before the runtime's ProcessExit kills the ffmpeg children.

**OS file drops on Linux.** Avalonia 11.3's X11 backend (used on X11 and XWayland) has no XDND, so file manager drops never arrive on Linux (`PlatformFeatures.OsFileDrops`). Drags inside the app work on every OS. On Linux the welcome target reads "Import media" and the empty bin points at Ctrl+I and the + button; the welcome target's "Import media..." button (every OS) opens the file dialog and then works like a drop.

**Change propagation.** `ProjectSession.Changed` is handled on the UI thread. View models compare the references they care about, the timeline calls `InvalidateVisual`, and the viewer re-requests the playhead frame.

### 10.2 Timeline control

`TimelineControl : Control` overrides `Render` and draws the current `Project` (live during gestures) and `TimelineViewState` in the order given in DESIGN 5.1. Filmstrip sheets are cached as `Bitmap`s (LRU of 64).

`HitTester` maps a point to `(zone, clipId, trackId, frame)` using the precedence in DESIGN 5.4.

```csharp
public interface IDragOperation
{
    void Begin(DragContext c);   // session.BeginGesture() unless a pure view drag (scrub, marquee)
    void Update(DragContext c);  // snapped delta -> session.UpdateGesture(start => edit)
    void Commit();               // session.CommitGesture(label)
    void Cancel();               // Esc -> session.CancelGesture()
    string? Tooltip { get; }     // "+00:00:01:12" / "Dur 00:00:04:03"
    ViewerHint? Hint { get; }    // edge frame or two-up frames
}
```

The implementations are Move, Trim (with a ripple flag), Roll, Slip, FadeHandle, Keyframe, VolumeLine, Marker, Marquee and Scrub, plus Playhead (dragging the playhead line in the lanes) and TrackResize (the header bottom edge). Esc reaches a running drag through `TimelineViewModel.EscapeCommand` when the window handles the key first.

**M4 scope.** The control also draws the track headers (target chip, name, eye, lock, M, S and the resize edge), so they stay aligned with the lanes; `TimelineView` adds the scroll bars. Since M6, keyframe dots and the volume line are hit only on selected audio clips, transition bands at least 24 px wide have 4 px resizable ends (`TransitionDrag`), marker heads in the lower ruler win over scrubbing, and fade handles exist only for the Select and Ripple tools (under Roll and Slip the corners act as the edges). `TimelineView` also hosts the inline track rename box and the marker editor; the header and lane context menus live in the control. Tracks are added and deleted through the toolbar "+ Track" menu of DESIGN 4.5 (add video, add audio, delete selected track) and the header context menu; an earlier corner "+" button duplicated the toolbar menu and was removed. Starting any lane drag (not a header gain or height drag) raises `TimelineViewModel.DragStarted`, which stops playback in `ViewerViewModel` (DESIGN 5.6). The drop target accepts media bin ids, Source ranges, effect ids and files (section 10.1).

`KeyBindingService` tunnels `KeyDown` on the main window and maps `(Key, KeyModifiers)` to commands through `DefaultKeymap` (DESIGN 7), respecting the focus rule. It detects the K plus J/L chords by tracking whether K is held.
- **Esc in a text input** is not taken in the tunnel: the input sees it first (the inspector's `commitOnEnter` boxes restore the model text, the `ScrubbyNumberBox` editor closes) and marks it handled. A second handler on the window's bubble route (`HandleUnclaimedKeyDown`) runs the Escape command only when the input left the key unhandled.
- **During a gesture** (`ProjectSession.InGesture` or `TimelineViewModel.IsDragging`) only S (snapping) and the zoom keys run; Esc goes on to the control, which cancels the drag; every other bound chord is claimed and dropped, so no edit, undo, paste, transport or file command can cancel the drag and edit the project under it. Keys outside the keymap (Ctrl, Shift, Alt for the drag modifiers) pass through.
- **Mouse buttons.** A lane drag or pending click starts only with the left button, and only a left-button release commits it (`PointerReleasedEventArgs.InitialPressMouseButton`); the same holds for `ScrubbyNumberBox`, which also ignores other presses while a press runs.

### 10.3 Frame presentation

`FramePresenter` implements `IVideoFrameSink` and keeps the newest frame with `Interlocked.Exchange`. On each `TopLevel.RequestAnimationFrame` callback, on the UI thread:
1. Take the scrub frame, or `PlaybackSession.TryTakeFrame` while playing.
2. Copy it into the back `WriteableBitmap(size, Vector(96,96), PixelFormat.Bgra8888, AlphaFormat.Premul)` row by row inside `using var fb = bmp.Lock()`: `Marshal.Copy(buf, y*stride, fb.Address + y*fb.RowBytes, w*4)`.
3. Swap the front and back bitmaps on `VideoSurface` and call `InvalidateVisual`.
4. Return pooled buffers.

ffmpeg `bgra` alpha is 255, so `Premul` is correct. Both bitmaps are recreated when the size changes (with Auto quality that is every play and stop, since playback is capped at 960x540), and the replaced pair is disposed 2 s later on the UI thread, once the compositor no longer draws it. Request the next animation frame only while playing or while a frame is pending.

**Viewer requests.** The viewer requests the playhead frame of the monitor shown (in Source mode the Source playhead on the one-clip Source snapshot). During a trim it requests `TimelineViewModel.DragHint.Frame` instead, the edge frame of DESIGN 4.3 (the roll and slip two-ups arrive with those drags in M6). It requests again when `MediaCatalog.IndexReady` reports a new frame index.

### 10.4 Command line and smoke mode

- `Cutlass.App <project.cutlass>` opens a project.
- `--import <file>...` imports files. Without a project they go into an untitled project, end to end on V1/A1, like a welcome drop.
- `--smoke-script <file> [--smoke-log <file>]` runs a developer smoke script after startup, then exits with 0 when every step passed and 1 otherwise (`Program.Main` returns the lifetime's exit code).
  - `SmokeScript` parses one command per line: `#` comments, quoted arguments, `name=value` options, and paths relative to the script.
  - `SmokeRunner` executes the steps on the UI thread through the view model commands the UI uses. `export` goes through `ExportDialogViewModel`, `frame` waits for `FramePresenter.FramePresented` with the viewer's latest request id, and `probe` runs ffprobe through `ProcessRunner`. It touches no controls: `screenshot` calls `MainWindow.CaptureAsync`, which runs a layout pass and renders the window with `RenderTargetBitmap` (without the layout pass a step that just changed the Inspector section was captured half laid out).
  - `SmokeDialogService` replaces the dialog service, so nothing waits for a person. Any message or error dialog fails the current step.
  - `SmokeLog` writes to stdout and the log file from a background task.
  - A 20 minute watchdog ends a hung run with code 1.
  - M5 adds `play [speed=1|2|4]` (Space, then L), `stop [max-drift=<frames>]`, `wait-jobs [timeout=s]` and `mute <track>`. While playing, the runner samples the clock frame, the last presented frame and the meter every 0.5 s. `stop` presses K, checks that the playhead parked on the last presented frame, and fails when the clock drifted from the wall clock by more than `max-drift` frames (default 2), measured from the first sample so the start latency does not count.
  - M6 adds `source-open`, `source-in`, `source-out`, `source-insert`, `source-overwrite`, `monitor`, `match-frame`, `key <chord>` (a press through `KeyBindingService`), `timecode <text>` (the click-to-type readout), `copy`, `cut`, `paste`, `trim-start`, `trim-end`, `add-transition`, `speed`, `select-gap`, `close-gap`, `add-track`, `expect-duration`, `expect-playhead`, `expect-source` and `expect-transitions`, and `expect-clips <count>` counts every track. Commands are run through the command registry, so menus, keys and the smoke run share one path. `build/smoke.ps1 -Script m6` runs `build/smoke/m6.smoke` on the m4 clips with an export whose frame count ffprobe checks.
  - The M6 verify run adds `source-drop video|audio|both <track> <pos> [insert]` (the Source payload round-trips through its drag text, then `PreviewSourcePlacements` and `PlaceSource`), `effect-drop <effect id> <track> <pos>` (`PreviewAt`, then `ApplyAt`), `select-transition <track> [index]`, `volume-key <track> <pos> <dB>`, `panel media|effects` and `expect-section <key prefix> [header=<text>]`. After the export, `m6.smoke` uses them for the DESIGN 5 checklist screenshots: a selected clip with fades and the Clip section, Source with In/Out after Source drops, a title with the Title section, a clamped transition with its red flag and notice, a gap selection, ruler markers, volume keyframes and the Effects tab. Mouse drags themselves (roll, slip, trims, fades) cannot be scripted and were checked by reading the drag classes against DESIGN 5.4 and 5.5.
  - The M5 verify run also uses `solo <track>`, `expect-meter [min=<dBFS>] [max=<dBFS>]` (the louder master meter channel, for live mute and solo), `expect-artifacts <media> [thumbs] [peaks] [proxy]` and `jobs open|close`. The jobs popover is its own window, so `screenshot` does not show it; `log CAPTURE <name>` lines mark moments for an outside screen capture.
- M7 adds `settings [key] [page=...] [<field>=<value>...] [expect-problem=<text>] [validate] [clear-cache] [apply|cancel]` (drives `SettingsViewModel`; `key` opens it with Ctrl+, through the keymap and takes the dialog the smoke dialog service was asked to show), `expect-setting <property> <value>` (in effect and in `settings.json`), `export ... height=<n>` (the custom size) and `width=<n>` (a GIF width), and `probe ... container=<name> novideo noaudio pixfmt=<fmt> tag=<fourcc> duration=<s>|export [tolerance=<s>]`. A run writes the settings it started with back at its end, so it never changes the user's settings (exports remember their folder and preset there).
- `build/smoke.ps1` generates two lavfi clips and runs `build/smoke/m4.smoke`. It needs a desktop session (use `xvfb-run` on Linux); CI runs it on both OSes, and the M7 script on Windows. `-AppPath <exe>` runs a published app instead of `dotnet run`, and `-Configuration` and `-NoBuild` pass through to `dotnet run`.
- M7 also adds `show-dialog settings|relink|export|speed|sequence [page=...] [media=<media>] screenshot=<file.png>`: it opens the dialog through the command the menus and keys use, takes the view model the smoke dialog service was asked to show (`SmokeDialogService.LastDialog`) and hands it to `SmokeRunner.CaptureDialog`, which the app sets to `WindowCapture.CaptureDialogAsync`: that opens the real dialog window over the main window (not modal), waits for `Opened` and a few dispatcher passes, renders it with `RenderTargetBitmap` after a layout pass and closes it. `remove-file <path>` deletes a file (retried while a process still holds it) and `expect-offline <media> [online]` checks the bin and timeline link state.
- `build/smoke.ps1 -Script m7b` generates the m7 clips plus `gone.mp4` and runs `build/smoke/m7b.smoke`: the three clips end to end, a save, `gone.mp4` deleted with the project closed, the project reopened (its clips hatched with the `OFFLINE` badge), then screenshots of the Relink dialog (offline and online rows), the four Settings pages, the export, speed and sequence settings dialogs for the DESIGN 8 visual check.
- `build/smoke.ps1 -Script m7` generates two 4 s 640x360 clips and runs `build/smoke/m7.smoke`: the Settings dialog (an invalid field, live defaults, the ffmpeg re-detect, the About page), a 120-frame sequence from Source ranges with a crossfade and a title, then one export per preset (section 9) with ffprobe checks. It takes about 10 s on the reference machine.
- `build/smoke.ps1 -Script m5` generates `clipA.mp4` (12 s, 1280x720), `clipB.mp4` (12 s, 2560x1440, so the Auto policy proxies it), both with loud amplitude-modulated tones, and `music.wav`. It starts the app with `--import clipA.mp4 clipB.mp4` and runs `build/smoke/m5.smoke`. That script places the music on A2, waits for the media jobs and checks thumbnails, peaks and the proxy. It plays 10 s across the cut into the proxied clip with the drift check, checks live mute and solo on the meter, and shuttles at 2x and 4x.
- A3 (AGENT_INTEGRATION 6.4 and 7.3) adds `--agent-server [--port N]` (agent connections for this run only) and the agent commands in `SmokeRunner.Agent.cs`: `agent-connect [name=<agent>]` (an in-process `AgentClient` from `endpoint.json` and the token file), `agent-call <tool> <json> [expect=<code>] [save=<var>=<path>]... [png=<file>] [until=<path>=<value> timeout=<s>]` (`POST /v1/call`; JSON strings in single quotes, `$var` and `$dir` substituted, the lock id kept after `lock.acquire`), `expect-locked [holder=<name>] [paused] [pending]`, `expect-unlocked`, `agent-takeover`, `agent-pause`, `agent-resume`, `expect-readonly [keys=<chord>,...]` (gated commands disabled, keys claimed, project and API revision unchanged), `expect-agent-log <tool> [code=<code>]`, `expect-no-refused`, `expect-duration-below <frames>`, `panel agent` and `settings ... agent-read=<folder> agent-write=<folder>`. A smoke run lets a paused agent call wait 1 s instead of 25 s. `build/smoke.ps1 -Script m9` generates the m4 clips plus `gap.mp4` (a tone with 1.5 s of silence) and runs `build/smoke/m9.smoke` with `--agent-server --port 47399`.
- The Agent menu (AGENT_INTEGRATION 6.6 and 9.4) adds `SmokeRunner.Mcp.cs`: `agent-briefing [http|target=<profile id>] [save=<file>]` (Agent > Copy briefing for your agent, which renders for the client registered last or the generic text, or a "Copy briefing for" item; then the clipboard text the smoke dialog service kept: every placeholder filled, the project named, the target's tail present: the tool names, the routes and token line, the harness contract or the client's registration; the saved copy masks the token), `menu-stop-mcp` and `menu-start-mcp` (Agent > Stop/Start MCP server through `RunCommandCommand`; the smoke dialog service answers the first-start dialog with Start and "Register with: none", so a smoke run registers no client; the server status, the badge and the menu text are checked), `show-menu <header> [submenu="<header>"] screenshot=<file>` (`WindowCapture.CaptureMenuAsync` draws the open menu's popup, and the open submenu's popup such as "Copy briefing for", over the window) and `show-dialog mcp-start|connect-clients [height=<px>]` (the first-start dialog and Agent > Connect an agent client...; `height=` renders a taller window, for example Settings > Agents with its Connect clients group).
- The agent clients (AGENT_INTEGRATION 9.3) add `SmokeRunner.Clients.cs`. A smoke run's client registry is always a sandbox: a fake home and `SmokeCliRunner`, which starts no process, so no real client configuration is read or written. `settings agent-home=<dir>` gives it an empty fake home inside the script folder and sets `$clienthome` and `$clientconfig` (the fake per-user configuration folder), `write-file <path> <text> [json]` seeds client files there, `agent-clients [expect=<id>:<state>,...] [current=<n>]` checks every profile again and compares the states, and `agent-client <id> register|update|remove [expect=<state>]` runs a row's button (the smoke dialog service confirms). `m9` ends with them.

---

## 11. Testing

### 11.1 Unit tests (`tests/Cutlass.Tests`)

- **Time:**
  - Exact `FlicksPerFrame` for every supported rate.
  - `SamplesAt` and `FrameAtSample` round trips with no drift over 24 h at 29.97.
  - Timecode, `SourceFlicksAt` with speeds.
- **Edits:** table tests per function, each result passing the validator. Cases include split at a boundary, partial overwrite, insert with a locked track, trim against a neighbour and the source end, ripple, roll and slip limits, ripple delete with collisions, close gap, speed with ripple, transition clamping, insert inside a transition, Alt single-side trim, and stills.
- **Fuzz:** 5,000 seeded random edits. The validator holds after each one, and undoing everything returns the initial reference.
- **Other Core:** `SnapEngine` (threshold across zooms, nearest candidate, excluded ids). Session and history (coalescing inside and outside 1.5 s, gestures, the 500 cap, dirty state). Serializer (round trip, golden file, unknown fields, migration). Number-formatting classes run under `de-DE`.
- **Compiler golden tests:**
  - Files: `tests/Cutlass.Tests/Compiler/Goldens/<case>.ffg` and `<case>.inputs.txt` (one input per line). `CUTLASS_UPDATE_GOLDEN=1` rewrites them.
  - Cases: single clip, gap first, cuts, each transition kind, a one-frame mid-transition window, title, PiP with negative position, crop and opacity, letterboxed fit, speeds 25%/50%/200%, envelope, fades with a window offset, still, mute/solo export, PerTrack audio, empty audio, audio-only, GIF, framestep, and section 7.8 byte for byte.
  - A determinism test compiles the same project twice.
  - Inputs: a duplicated clip gets two inputs, linked audio and video share one, different speeds never share. The atempo head trim follows its calibration table, and a pre-roll before the file start is padded with `adelay`.
- **Parsers:**
  - ffprobe fixtures: rotated phone video, VFR, HDR, audio-only, PNG, attached picture, missing duration.
  - Packet index with `N/A` and `D` flags.
  - `-version` output from N- and 6.1 builds, `-encoders`, `-filters`.
  - Progress, including split reads and `speed= 5.7x`.
- **Other:** `FilterEscaper` rejecting non-bare names, peak pyramid, LRU, locator order (fake file system), preset greying.
- **Media robustness:** the orphan sweep decision with fake process facts (live owner, gone owner, reused pid, other boot), the feeder skipping an empty PCM block (fake output that rejects it like OpenAL), the clock falling back to the Stopwatch when the output is lost, the mixer's Premixed flag and state set from two threads, file identity and kind, and remote media detection (UNC, network drives, `/proc/self/mounts`).

### 11.2 Integration tests (`tests/Cutlass.IntegrationTests`)

`[FfmpegFact]` derives from `FactAttribute` and sets `Skip` in its constructor when no ffmpeg is found, because xunit 2 has no runtime skip. A collection fixture generates the media once:

```
color=c=black:s=320x180:r=25:d=20,geq=lum='if(bitand(N\,pow(2\,floor(X/40)+8*floor(Y/90)))\,235\,16)':cb=128:cr=128,format=yuv420p
   -> oracle25_intra.mkv (ffv1); oracle25_lgop.mp4 (libx264 -g 250 -bf 3 -crf 18, else mpeg4 -g 250 -bf 2 -q:v 2)
   -> oracle2997.mkv (r=30000/1001); oracle_vfr.mkv (-vf "settb=1/1000,setpts='(N/25+0.01*mod(N\,3))/TB'" -fps_mode vfr -enc_time_base 1/1000, ffv1)
      (without settb and -enc_time_base the 1/25 time bases round the 10 ms offsets away and the file is CFR)
aevalsrc='if(eq(mod(n\,48000)\,0)\,0.9\,0)':s=48000:d=20      -> impulse every second
testsrc2=s=320x180:r=30:d=3 + -itsoffset 0.5 impulse            -> delayed_audio.mov (pcm_s16le)
flash on frames with mod(N,30)=0 + impulse                        -> sync.mkv
sine=f=1000:r=48000:d=10 -> tone.wav;  color=c=red:s=320x240 -frames:v 1 -> red.png
probe and process media (M2), testsrc2=s=320x180 + sine, mpeg4 video:
   -> cfr25_av.mp4 (r=25, aac 48 kHz stereo); cfr2997_av.mkv (r=30000/1001, pcm_s16le 44.1 kHz mono)
   -> offset_start.mov (-output_ts_offset 2.5, so format.start_time is 2.5)
   -> vfr_mixed.mp4 (setpts: 30 frames at 30 fps then 60 at 10 fps, -fps_mode vfr, r_frame_rate 30 vs avg ~12.9)
   -> rotated90.mp4 (remux with the input option -display_rotation 90; -metadata:s:v rotate no longer writes a display matrix)
   -> "it's, [odd] Ä 日本/clip ü [1] it's.mp4" (a copy of cfr25_av.mp4 under an odd path)
aevalsrc: 100 ms Hann-windowed 997 Hz bursts every 24011 samples from 48000, d=12 -> bursts.wav (atempo calibration)
oracle source, -c:v mpeg4 -bf 2 -q:v 2                            -> oracle25_bf.avi (packets without pts)
```

**Oracle decoding.** Threshold the centre of each 40x90 block at 128 in gray or BGRA output, which reads 16 bits.

**Assertions:**
- **Probe:** correct fields. Capability detection reports Slash on master and LegacyScript on 6.1.
- **Frame accuracy:** scrubbing returns `expected(k)` at 50 random frames for every oracle, through both original and proxy, at 50%, 100% and 200% speed.
- **Range independence:** a one-frame window is byte-identical to the full render, including a mid-transition frame.
- **Counts:** exports have exactly the planned frame count (`ffprobe -count_frames`) and sample count. Playback window seams have no gap and no duplicate.
- **A/V offset:** the `delayed_audio.mov` impulse lands at sample 24000 (seek 0) and 14400 (seek 0.2). The sync clip stays within 1 ms after export. Its waveform's first impulse is peak 50 (0.5 s), not 0.
- **Speed:** tone bursts through atempo land within 1 ms on average (each within 6 ms) at 25, 50, 150, 200 and 300 percent, in an export and across 37-frame playback windows (`AtempoAlignmentTests`).
- **Inputs:** a clip used twice 10 s apart gets two inputs and shows the right oracle frames, flashes and impulses in both places (`InputSharingTests`).
- **Frame index:** the B-frame AVI gets no index, and the CFR fallback shows what the decoder shows.
- **Levels:** envelope RMS within 1 dB. The limiter leaves unclipped levels alone.
- **Formats and previews:** BT.709 tags are present. Every available preset decodes. PerTrack audio has 2N channels and the expected bytes.
- **Proxies:** the mapping is `exact` for the CFR oracles.
- **Robustness:**
  - A title whose font and text live under `it's, [odd] Ä` renders.
  - Cancel leaves no partial file and no processes.
  - A target held open keeps the finished export as `<name> (n).<ext>`; a stale partial file survives; an output spelled differently from a project media file is refused (`ExportOutputSafetyTests`).
  - A second instance's sweep leaves a live owner's children alone; a gone owner's are killed (`ChildProcessRegistryTests`).
  - A proxy deleted behind the catalog's back is detached and queued again while scrubbing continues.
  - Unsafe font paths (folder, wrong extension, over 64 MiB, a device) take the fallback without being read.
  - Disposing a playback session kills its children within 1 s.
  - The cache survives a new service instance and a planted temp file.

### 11.3 CI

`.github/workflows/ci.yml` runs a matrix of `windows-latest` (a pinned gyan.dev ffmpeg 7.1 release build, which has every preset encoder and the `-/filter_complex` syntax) and `ubuntu-24.04` (`apt-get install -y ffmpeg`, which is 6.1 and exercises `-filter_complex_script`). Each leg restores, builds `-c Release` (warnings are errors), runs the unit and integration tests on that build (the Windows leg sets `CUTLASS_SKIP_REALTIME=1`, which skips the `[RealtimeFact]` playback tests that judge dropped frames and A/V lag in real time: the shared Windows runners stall for up to a second at a time; Ubuntu runs them), `build/check-text.ps1`, and the M4 smoke run (`build/smoke.ps1 -Script m4 -Configuration Release -NoBuild`; on Linux under `xvfb-run`), uploading the smoke logs and screenshots. The Windows leg also runs the M7 smoke (`-Script m7`, the Settings dialog and one export per preset), which needs the full build's encoders; the Ubuntu leg stays on M4. On a `v*` tag a second job runs `build/publish.sh` for win-x64 and linux-x64 and uploads each folder as a tarball (which keeps the execute bit).

---

## 12. Build order

Each step ends green and runnable. Never start a step before the previous one verifies.

1. **M0 Skeleton.** The slnx, props files, all six projects and the dark theme. `MainWindow` shows the final grid with placeholder panels, splitters and the status bar. One trivial test and CI.
   *Verify:* the build command passes, `dotnet test tests/Cutlass.Tests --disable-build-servers -m:1` runs, and the app launches on Windows (and on Linux when available).
2. **M1 Core.** Time, model, every edit, the validator, snapping, the session and undo, serialization.
   *Verify:* unit suites, fuzz and de-DE runs pass.
3. **M2 ffmpeg foundation.** AppPaths, settings, the locator, `CapabilityProbe` (including script syntax), `ProcessRunner` with the Job Object and registry, the work directory and fonts, the probe parser and service, the integration fixture.
   *Verify:* integration tests for probe, capabilities, kill and the odd path.
4. **M3 Compiler and headless export.** The full compiler, presets, `FfmpegArgsBuilder`, `ExportService`, `ProgressParser`.
   *Verify:* golden and determinism tests, then integration tests for exact counts, oracle accuracy, the delayed-audio offset, window equality and cancel. **The engine is proven before any UI.**
5. **M4 Vertical slice.** Import with probe and poster, `TimelineControl` (render, zoom, scroll, select, move, trim, razor, delete, snapping, undo), FrameServer scrub (originals and CFR math), the viewer with `FramePresenter`, the export dialog and jobs popover, save/open with the sidecar, and the welcome overlay.
   *Verify:* manual smoke test. Import two clips, cut, rearrange, scrub and check frames, then export. The file plays elsewhere and matches the viewer. `build/smoke.ps1` automates the import, cut, scrub, undo, save/open and export checks, including the ffprobe count and tags (section 10.4). Watching the file in another player and the mouse interactions stay manual.
6. **M5 Cache and playback.** Fingerprint, cache, scheduler, frame index, thumbnails (filmstrips, skim, placeholder), waveforms, proxies, `FrameCache` and prefetch, OpenAL, `PlaybackSession`, `AudioMixer` with the master meter, L at 1x, 2x and 4x.
   *Verify:* proxy and seam integration tests, 2 minutes of sync-clip playback without drift, and live mute and solo. The integration tests check all three headless (the 2 minute test prints its drift against the wall clock, the A/V lag and the impulse offsets). `build/smoke.ps1 -Script m5` checks playback on the audio device, the master meter and live mute and solo in the real app (section 10.4).
7. **M6 Editing depth.**
   - Ripple, Roll and Slip tools, Q/W, the Effects tab and transitions, titles, the inspector with ScrubbyNumberBox and coalescing.
   - Fade and keyframe drags, markers, In/Out, Source mode and three-point edits, match frame, copy and paste, link and sync badges.
   - Gap selection, track header controls, add/remove tracks, the speed dialog.

   *Verify:* view model tests and the DESIGN section 5 checklist by hand.
8. **M7 Hardening.** Autosave and recovery, relink, the settings dialog, recent projects, error dialogs, cache eviction, every preset, publish scripts (`dotnet publish src/Cutlass.App -c Release -r win-x64|linux-x64 --self-contained`), and a Linux run (X11 and XWayland).
   *Verify:* CI green on both OSes and the text check clean. Locally: `build/smoke.ps1 -Script m7` (settings and every preset), `build/smoke.ps1 -Script m7b` (offline media and the dialog screenshots), and `build/publish.ps1` followed by `build/smoke.ps1 -AppPath artifacts/publish/win-x64/Cutlass.App.exe` (the M4 run on the published app); linux-x64 cross-publishes from Windows but runs only on Linux.
9. **M8 Late items (optional for v1).** Reverse shuttle, per-track meters, hardware encoders, segmented export.

---

**TLDR:**
- **Projects.** Core (immutable model, clamping pure edits, gesture and coalescing undo, DTO JSON), Compiler (pure, deterministic filtergraph generation), Media (processes, cache, jobs, preview, export, OpenAL) and App (Avalonia MVVM with one custom timeline control).
- **Frame and sample exactness:** ordinal-based seeks floored to the microsecond, `fps=R:start_time=0:round=up`, exact `tpad`/`trim` and `apad`/`atrim` counts, and `aresample first_pts=0` for A/V offsets.
- **Compositing:** a black base plus delayed runs overlaid with `eof_action=pass`, and audio placed with `adelay` and summed with `amix`.
- **Range windows** take the same path and are byte-identical to full renders.
- **Build order:** skeleton, core, ffmpeg foundation, headless export, vertical slice, playback, editing depth, hardening, late items.
