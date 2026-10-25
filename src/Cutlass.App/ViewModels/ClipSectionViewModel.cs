using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The inspector for media clips (DESIGN 4.4): Clip (name, file, enabled, start, duration, source in and out, speed),
/// Transform, Color, Effects, Chroma Key, Fades, Audio and Audio Effects. With several clips selected only fields they share show, and an edit applies to
/// all of them as one undo step: the video groups act on the selected video clips and the audio groups on the selected
/// audio clips, so a linked video and audio pair shows both. Selected titles take part in the Clip rows only.
/// </summary>
public sealed partial class ClipSectionViewModel : InspectorClipSectionBase
{
    private readonly ImmutableArray<string> _videoIds;
    private readonly ImmutableArray<string> _audioIds;
    private readonly ImmutableArray<string> _sourceIds;
    private readonly ImmutableArray<string> _freezeIds;
    private readonly List<InspectorNumberField> _videoFields = [];
    private readonly List<InspectorNumberField> _audioFields = [];
    private readonly List<(InspectorToggleRow Row, Func<VideoProps, bool> Read)> _videoToggles = [];
    private readonly List<(InspectorToggleRow Row, Func<AudioProps, bool> Read)> _audioToggles = [];
    private readonly InspectorGroup _clipGroup;
    private readonly InspectorGroup _transformGroup;
    private readonly InspectorGroup _colorGroup;
    private readonly InspectorGroup _effectsGroup;
    private readonly InspectorGroup _keyGroup;
    private readonly InspectorColorField _keyColorRow;
    private readonly InspectorGroup _fadesGroup;
    private readonly InspectorGroup _audioGroup;
    private readonly InspectorGroup _audioFxGroup;
    private readonly InspectorInfoRow _fileRow;
    private readonly InspectorTimecodeField _sourceInRow;
    private readonly InspectorTimecodeField _sourceOutRow;
    private readonly InspectorButtonRow _speedRow;
    private readonly InspectorButtonRow _addKeyRow;
    private readonly List<InspectorKeyframeRow> _keyRows = [];

    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="project">The project the selection was made in.</param>
    /// <param name="clipIds">The selected clip ids (existing, sorted).</param>
    /// <param name="openSpeedDialog">Opens the speed dialog (Ctrl+R).</param>
    public ClipSectionViewModel(
        InspectorEditor editor, IDictionary<string, bool> states, Project project, IReadOnlyList<string> clipIds, ICommand openSpeedDialog)
        : base(editor, states, "clip:" + string.Join(",", clipIds), Caption(clipIds.Count), clipIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(openSpeedDialog);
        var clips = ClipsOf(project);
        _videoIds = [.. clips.OfType<MediaClip>().Where(m => m.Stream == StreamKind.Video).Select(m => m.Id)];
        _audioIds = [.. clips.OfType<MediaClip>().Where(m => m.Stream == StreamKind.Audio).Select(m => m.Id)];
        // Clips with a source range once they play (frozen ones included: the toggle can unfreeze them in place, so
        // Refresh decides the Source rows' visibility from the current freeze flags).
        _sourceIds = [.. clips.OfType<MediaClip>().Where(m => TimelineQuery.IsBounded(m with { Freeze = false }, TimelineQuery.FindMedia(project, m.MediaId))).Select(m => m.Id)];
        _freezeIds = [.. clips.OfType<MediaClip>().Where(m => m.Stream == StreamKind.Video && !IsStill(project, m)).Select(m => m.Id)];

        _clipGroup = AddGroup("CLIP");
        _clipGroup.Add(NameRow);
        _fileRow = _clipGroup.Add(new InspectorInfoRow("File"));
        _clipGroup.Add(EnabledRow);
        _clipGroup.Add(StartRow);
        _clipGroup.Add(DurationRow);
        _sourceInRow = _clipGroup.Add(new InspectorTimecodeField(editor, "Source in", Key2(_sourceIds, "clip.sourceIn"), SourceInEdit));
        _sourceOutRow = _clipGroup.Add(new InspectorTimecodeField(editor, "Source out", Key2(_sourceIds, "clip.sourceOut"), SourceOutEdit));
        _speedRow = _clipGroup.Add(new InspectorButtonRow("Speed", "100%", openSpeedDialog) { ToolTip = "Speed (Ctrl+R)" });
        // Effects batch 4: the freeze flag of the selected video clips (stills are always a single frame, so they hide it).
        // It writes only to those clips, never to a still in a mixed selection.
        FreezeRow = _clipGroup.Add(new InspectorToggleRow(editor, "Freeze frame", Key2(_freezeIds, "clip.freeze"), on => UpdateFreeze(on))
        {
            ToolTip = "Holds the source in point for the whole clip (Shift+F inserts a hold at the playhead)",
        });

        _transformGroup = AddGroup("TRANSFORM", () => Run(UpdateVideo("Reset Transform", m => m with
        {
            Video = m.Video with { Transform = Transform.Identity, Opacity = 1 },
        })));
        Video(_transformGroup, "Position X", "video.transform.x", "Position", c => c.Transform.X, (m, v) => T(m, t => t with { X = v }), 0, -100_000, 100_000, 1, 0, "px");
        Video(_transformGroup, "Position Y", "video.transform.y", "Position", c => c.Transform.Y, (m, v) => T(m, t => t with { Y = v }), 0, -100_000, 100_000, 1, 0, "px");
        Video(_transformGroup, "Scale", "video.transform.scale", "Scale", c => c.Transform.Scale * 100, (m, v) => T(m, t => t with { Scale = v / 100 }), 100, 1, 10_000, 0.5, 1, "%");
        Video(_transformGroup, "Crop left", "video.transform.cropLeft", "Crop", c => c.Transform.CropLeft * 100, (m, v) => T(m, t => t with { CropLeft = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Crop top", "video.transform.cropTop", "Crop", c => c.Transform.CropTop * 100, (m, v) => T(m, t => t with { CropTop = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Crop right", "video.transform.cropRight", "Crop", c => c.Transform.CropRight * 100, (m, v) => T(m, t => t with { CropRight = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Crop bottom", "video.transform.cropBottom", "Crop", c => c.Transform.CropBottom * 100, (m, v) => T(m, t => t with { CropBottom = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Opacity", "video.opacity", "Opacity", c => c.Opacity * 100, (m, v) => m with { Video = m.Video with { Opacity = v / 100 } }, 100, 0, 100, 0.5, 0, "%");

        _colorGroup = AddGroup("COLOR", () => Run(UpdateVideo("Reset Color", m => m with { Video = m.Video with { Color = ColorAdjust.Neutral } })));
        Video(_colorGroup, "Brightness", "color.brightness", "Brightness", c => c.Color.Brightness, (m, v) => C(m, a => a with { Brightness = v }), 0, -1, 1, 0.005, 2, null);
        Video(_colorGroup, "Contrast", "color.contrast", "Contrast", c => c.Color.Contrast, (m, v) => C(m, a => a with { Contrast = v }), 1, 0, 2, 0.005, 2, null);
        Video(_colorGroup, "Saturation", "color.saturation", "Saturation", c => c.Color.Saturation, (m, v) => C(m, a => a with { Saturation = v }), 1, 0, 3, 0.01, 2, null);
        Video(_colorGroup, "Gamma", "color.gamma", "Gamma", c => c.Color.Gamma, (m, v) => C(m, a => a with { Gamma = v }), 1, 0.1, 4, 0.01, 2, null);

        // Effects batch 2 (DESIGN 4.4): the static effects and the chroma key, each with its own Reset. Percent rows
        // convert like Opacity; the model keeps 0..1 (or -1..1 for the tint).
        _effectsGroup = AddGroup("EFFECTS", () => Run(UpdateVideo("Reset Effects", m => m with { Video = m.Video with { Effects = VideoEffects.None } })));
        FlipHRow = VideoToggle(_effectsGroup, "Flip horizontal", "video.effects.flipH", "Flip Horizontal", c => c.Effects.FlipH, (m, on) => E(m, e => e with { FlipH = on }));
        FlipVRow = VideoToggle(_effectsGroup, "Flip vertical", "video.effects.flipV", "Flip Vertical", c => c.Effects.FlipV, (m, on) => E(m, e => e with { FlipV = on }));
        Video(_effectsGroup, "Rotation", "video.effects.rotation", "Rotation", c => c.Effects.RotationDegrees, (m, v) => E(m, e => e with { RotationDegrees = v }), 0, -180, 180, 0.5, 1, "°");
        Video(_effectsGroup, "Hue", "video.effects.hue", "Hue", c => c.Effects.HueDegrees, (m, v) => E(m, e => e with { HueDegrees = v }), 0, -180, 180, 0.5, 1, "°");
        Video(_effectsGroup, "Temperature", "video.effects.temperature", "Temperature", c => c.Effects.TemperatureKelvin, (m, v) => E(m, e => e with { TemperatureKelvin = v }), 6500, 1000, 40000, 50, 0, "K");
        Video(_effectsGroup, "Tint", "video.effects.tint", "Tint", c => c.Effects.Tint * 100, (m, v) => E(m, e => e with { Tint = v / 100 }), 0, -100, 100, 0.5, 0, "%");
        Video(_effectsGroup, "Blur", "video.effects.blur", "Blur", c => c.Effects.Blur, (m, v) => E(m, e => e with { Blur = v }), 0, 0, 50, 0.1, 1, "px");
        Video(_effectsGroup, "Sharpen", "video.effects.sharpen", "Sharpen", c => c.Effects.Sharpen * 100, (m, v) => E(m, e => e with { Sharpen = v / 100 }), 0, 0, 100, 0.5, 0, "%");
        Video(_effectsGroup, "Vignette", "video.effects.vignette", "Vignette", c => c.Effects.Vignette * 100, (m, v) => E(m, e => e with { Vignette = v / 100 }), 0, 0, 100, 0.5, 0, "%");
        GrayscaleRow = VideoToggle(_effectsGroup, "Grayscale", "video.effects.grayscale", "Grayscale", c => c.Effects.Grayscale, (m, on) => E(m, e => e with { Grayscale = on }));

        _keyGroup = AddGroup("CHROMA KEY", () => Run(UpdateVideo("Reset Chroma Key", m => E(m, e => e with
        {
            KeyEnabled = VideoEffects.None.KeyEnabled,
            KeyColor = VideoEffects.None.KeyColor,
            KeySimilarity = VideoEffects.None.KeySimilarity,
            KeyBlend = VideoEffects.None.KeyBlend,
        }))));
        KeyEnabledRow = VideoToggle(_keyGroup, "Key enabled", "video.effects.keyEnabled", "Chroma Key", c => c.Effects.KeyEnabled, (m, on) => E(m, e => e with { KeyEnabled = on }));
        // The colour field normalizes to #RRGGBBAA; the key colour is opaque, so the model keeps the #RRGGBB part.
        _keyColorRow = _keyGroup.Add(new InspectorColorField(editor, "Key color", Key2(_videoIds, "video.effects.keyColor"), c => UpdateVideo("Key Color", m => E(m, e => e with { KeyColor = c[..7] }))));
        Video(_keyGroup, "Similarity", "video.effects.keySimilarity", "Key Similarity", c => c.Effects.KeySimilarity * 100, (m, v) => E(m, e => e with { KeySimilarity = v / 100 }), 10, 1, 100, 0.5, 0, "%");
        Video(_keyGroup, "Blend", "video.effects.keyBlend", "Key Blend", c => c.Effects.KeyBlend * 100, (m, v) => E(m, e => e with { KeyBlend = v / 100 }), 0, 0, 100, 0.5, 0, "%");

        _fadesGroup = AddGroup("FADES", () => Run(UpdateVideo("Reset Fades", m => m with { Video = m.Video with { FadeInFrames = 0, FadeOutFrames = 0 } })));
        Video(_fadesGroup, "Fade in", "video.fadeIn", "Fade", c => c.FadeInFrames, (m, v) => m with { Video = m.Video with { FadeInFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");
        Video(_fadesGroup, "Fade out", "video.fadeOut", "Fade", c => c.FadeOutFrames, (m, v) => m with { Video = m.Video with { FadeOutFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");

        // The AUDIO EFFECTS group has its own Reset, so this one keeps the effects (gain, fades and keyframes reset).
        _audioGroup = AddGroup("AUDIO", () => Run(UpdateAudio("Reset Audio", m => m with { Audio = AudioProps.Default with { Effects = m.Audio.Effects } })));
        Audio("Gain", "audio.gain", "Gain", a => a.GainDb, (m, v) => m with { Audio = m.Audio with { GainDb = v } }, 0, TimelineEdits.MinGainDb, TimelineEdits.MaxGainDb, 0.1, 1, "dB");
        Audio("Fade in", "audio.fadeIn", "Fade", a => a.FadeInFrames, (m, v) => m with { Audio = m.Audio with { FadeInFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");
        Audio("Fade out", "audio.fadeOut", "Fade", a => a.FadeOutFrames, (m, v) => m with { Audio = m.Audio with { FadeOutFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");
        _addKeyRow = _audioGroup.Add(new InspectorButtonRow("Keyframes", "Add at playhead", new RelayCommand(AddKeyframe))
        {
            ToolTip = "Adds a volume keyframe at the playhead (Ctrl+click on the volume line does the same)",
        });

        // Effects batch 3 (DESIGN 4.4): the static audio effects in their own group, after AUDIO so the keyframe button
        // stays the last AUDIO row. Thresholds are dBFS, the pan row is percent over the model's -1..1.
        _audioFxGroup = AddGroup("AUDIO EFFECTS", () => Run(UpdateAudio("Reset Audio Effects", m => m with { Audio = m.Audio with { Effects = AudioEffects.None } })));
        Audio(_audioFxGroup, "High-pass", "audio.effects.highPass", "High-pass", a => a.Effects.HighPassHz, (m, v) => AE(m, e => e with { HighPassHz = v }), 0, 0, 2000, 10, 0, "Hz");
        Audio(_audioFxGroup, "Low-pass", "audio.effects.lowPass", "Low-pass", a => a.Effects.LowPassHz, (m, v) => AE(m, e => e with { LowPassHz = v }), 0, 0, 20000, 100, 0, "Hz");
        Audio(_audioFxGroup, "Bass", "audio.effects.bass", "Bass", a => a.Effects.BassDb, (m, v) => AE(m, e => e with { BassDb = v }), 0, -20, 20, 0.1, 1, "dB");
        Audio(_audioFxGroup, "Treble", "audio.effects.treble", "Treble", a => a.Effects.TrebleDb, (m, v) => AE(m, e => e with { TrebleDb = v }), 0, -20, 20, 0.1, 1, "dB");
        CompressorRow = AudioToggle(_audioFxGroup, "Compressor", "audio.effects.compressor", "Compressor", a => a.Effects.Compressor, (m, on) => AE(m, e => e with { Compressor = on }));
        Audio(_audioFxGroup, "Threshold", "audio.effects.compressorThreshold", "Compressor Threshold", a => a.Effects.CompressorThresholdDb, (m, v) => AE(m, e => e with { CompressorThresholdDb = v }), -20, -60, 0, 0.5, 1, "dB");
        Audio(_audioFxGroup, "Ratio", "audio.effects.compressorRatio", "Compressor Ratio", a => a.Effects.CompressorRatio, (m, v) => AE(m, e => e with { CompressorRatio = v }), 4, 1, 20, 0.1, 1, ":1");
        GateRow = AudioToggle(_audioFxGroup, "Gate", "audio.effects.gate", "Gate", a => a.Effects.Gate, (m, on) => AE(m, e => e with { Gate = on }));
        Audio(_audioFxGroup, "Gate threshold", "audio.effects.gateThreshold", "Gate Threshold", a => a.Effects.GateThresholdDb, (m, v) => AE(m, e => e with { GateThresholdDb = v }), -40, -80, 0, 0.5, 1, "dB");
        Audio(_audioFxGroup, "Pan", "audio.effects.pan", "Pan", a => a.Effects.Pan * 100, (m, v) => AE(m, e => e with { Pan = v / 100 }), 0, -100, 100, 1, 0, "%");
        MonoRow = AudioToggle(_audioFxGroup, "Mono", "audio.effects.mono", "Mono", a => a.Effects.Mono, (m, on) => AE(m, e => e with { Mono = on }));

        _transformGroup.IsVisible = !_videoIds.IsEmpty;
        _colorGroup.IsVisible = !_videoIds.IsEmpty;
        _effectsGroup.IsVisible = !_videoIds.IsEmpty;
        _keyGroup.IsVisible = !_videoIds.IsEmpty;
        _fadesGroup.IsVisible = !_videoIds.IsEmpty;
        _audioGroup.IsVisible = !_audioIds.IsEmpty;
        _audioFxGroup.IsVisible = !_audioIds.IsEmpty;
        _sourceInRow.IsVisible = clips.OfType<MediaClip>().Any(m => !m.Freeze && _sourceIds.Contains(m.Id));
        _sourceOutRow.IsVisible = _sourceInRow.IsVisible;
        FreezeRow.IsVisible = !_freezeIds.IsEmpty;
        _fileRow.IsVisible = clips.Length == 1 && clips[0] is MediaClip;
        _addKeyRow.IsVisible = _audioIds.Length == 1;
    }

    /// <summary>The selected video media clip ids (Transform, Color and Fades act on these).</summary>
    public ImmutableArray<string> VideoClipIds => _videoIds;

    /// <summary>The selected audio media clip ids (Audio acts on these).</summary>
    public ImmutableArray<string> AudioClipIds => _audioIds;

    /// <summary>Every numeric field of the video groups.</summary>
    public IReadOnlyList<InspectorNumberField> VideoFields => _videoFields;

    /// <summary>Every numeric field of the audio groups (keyframe rows excluded).</summary>
    public IReadOnlyList<InspectorNumberField> AudioFields => _audioFields;

    /// <summary>Every toggle row of the Audio Effects group (compressor, gate, mono).</summary>
    public IReadOnlyList<InspectorToggleRow> AudioToggles => [.. _audioToggles.Select(t => t.Row)];

    /// <summary>The Compressor toggle.</summary>
    public InspectorToggleRow CompressorRow { get; }

    /// <summary>The Gate toggle.</summary>
    public InspectorToggleRow GateRow { get; }

    /// <summary>The Mono toggle.</summary>
    public InspectorToggleRow MonoRow { get; }

    /// <summary>Every toggle row of the video groups (flips, grayscale, chroma key enabled).</summary>
    public IReadOnlyList<InspectorToggleRow> VideoToggles => [.. _videoToggles.Select(t => t.Row)];

    /// <summary>The Flip horizontal toggle.</summary>
    public InspectorToggleRow FlipHRow { get; }

    /// <summary>The Flip vertical toggle.</summary>
    public InspectorToggleRow FlipVRow { get; }

    /// <summary>The Grayscale toggle.</summary>
    public InspectorToggleRow GrayscaleRow { get; }

    /// <summary>The chroma key Enabled toggle.</summary>
    public InspectorToggleRow KeyEnabledRow { get; }

    /// <summary>The chroma key colour row.</summary>
    public InspectorColorField KeyColorRow => _keyColorRow;

    /// <summary>The keyframe rows of a single selected audio clip.</summary>
    public IReadOnlyList<InspectorKeyframeRow> KeyframeRows => _keyRows;

    /// <summary>The speed button row.</summary>
    public InspectorButtonRow SpeedRow => _speedRow;

    /// <summary>The Freeze frame toggle (video media clips that are not stills).</summary>
    public InspectorToggleRow FreezeRow { get; }

    /// <summary>The source in row.</summary>
    public InspectorTimecodeField SourceInRow => _sourceInRow;

    /// <summary>The source out row.</summary>
    public InspectorTimecodeField SourceOutRow => _sourceOutRow;

    /// <summary>The Transform group.</summary>
    public InspectorGroup TransformGroup => _transformGroup;

    /// <summary>The Color group.</summary>
    public InspectorGroup ColorGroup => _colorGroup;

    /// <summary>The Effects group.</summary>
    public InspectorGroup EffectsGroup => _effectsGroup;

    /// <summary>The Chroma Key group.</summary>
    public InspectorGroup ChromaKeyGroup => _keyGroup;

    /// <summary>The video Fades group.</summary>
    public InspectorGroup FadesGroup => _fadesGroup;

    /// <summary>The Audio group.</summary>
    public InspectorGroup AudioGroup => _audioGroup;

    /// <summary>The Audio Effects group.</summary>
    public InspectorGroup AudioEffectsGroup => _audioFxGroup;

    /// <summary>Finds a numeric field by label in the video or audio groups.</summary>
    /// <param name="label">The label, for example "Saturation".</param>
    /// <param name="audio">True to search the audio groups.</param>
    /// <returns>The field.</returns>
    public InspectorNumberField Field(string label, bool audio = false) =>
        (audio ? _audioFields : _videoFields).First(f => f.Label == label);

    /// <inheritdoc />
    public override void Refresh(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var clips = ClipsOf(project);
        var rate = project.Sequence.Settings.Rate;
        RefreshClipRows(project, clips);
        _fileRow.Text = clips.Length == 1 && clips[0] is MediaClip single
            ? TimelineQuery.FindMedia(project, single.MediaId)?.Path ?? "(missing media)"
            : string.Empty;

        // Frozen clips have no source range; the rows hide when every source clip is frozen (or a still), and the
        // section is reused when the Freeze frame toggle changes that in place, so it is decided on every refresh.
        var sources = clips.OfType<MediaClip>().Where(m => !m.Freeze && _sourceIds.Contains(m.Id)).ToList();
        var ins = sources.Select(m => SourceInFrame(m, rate)).Distinct().ToList();
        var outs = sources.Select(m => SourceOutFrame(m, rate)).Distinct().ToList();
        _sourceInRow.Load(ins.Count == 1 ? ins[0] : null, rate);
        _sourceOutRow.Load(outs.Count == 1 ? outs[0] : null, rate);
        _sourceInRow.IsVisible = sources.Count > 0;
        _sourceOutRow.IsVisible = sources.Count > 0;
        // Stills and frozen clips have no speed; the row hides when the selection holds nothing else (frozen clips can
        // change in place through the Freeze frame toggle, so this is decided on every refresh).
        var moving = clips.OfType<MediaClip>().Where(m => !IsStill(project, m) && !m.Freeze).ToList();
        var speeds = moving.Select(m => m.SpeedPercent).Distinct().ToList();
        _speedRow.ButtonText = speeds.Count == 1 ? speeds[0].ToString(CultureInfo.InvariantCulture) + "%" : "Mixed";
        _speedRow.IsVisible = moving.Count > 0;
        FreezeRow.LoadMany(clips.OfType<MediaClip>().Where(m => _freezeIds.Contains(m.Id)).Select(m => m.Freeze));

        var videos = clips.Where(c => _videoIds.Contains(c.Id)).ToList();
        foreach (var field in _videoFields)
        {
            field.LoadFrom(videos);
        }

        // Toggle and colour rows have no Read like the number fields, so they load here (as the title section does).
        var videoProps = videos.OfType<MediaClip>().Select(m => m.Video).ToList();
        foreach (var (row, read) in _videoToggles)
        {
            row.LoadMany(videoProps.Select(read));
        }

        _keyColorRow.LoadMany(videoProps.Select(v => v.Effects.KeyColor));

        var audios = clips.Where(c => _audioIds.Contains(c.Id)).ToList();
        foreach (var field in _audioFields)
        {
            field.LoadFrom(audios);
        }

        var audioProps = audios.OfType<MediaClip>().Select(m => m.Audio).ToList();
        foreach (var (row, read) in _audioToggles)
        {
            row.LoadMany(audioProps.Select(read));
        }

        RefreshKeyframes(audios.Count == 1 ? audios[0] as MediaClip : null, rate);
    }

    /// <summary>The source in point shown for a clip, in sequence frames of source time.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The frame.</returns>
    public static long SourceInFrame(MediaClip clip, FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return TimeMath.FloorDiv(clip.SourceInFlicks, rate.FlicksPerFrame);
    }

    /// <summary>The source out point (exclusive) shown for a clip: source in plus the duration at the clip's speed.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The frame.</returns>
    public static long SourceOutFrame(MediaClip clip, FrameRate rate)
    {
        ArgumentNullException.ThrowIfNull(clip);
        return SourceInFrame(clip, rate) + (long)Math.Round(clip.Duration * clip.SpeedPercent / 100.0, MidpointRounding.AwayFromZero);
    }

    private static string Caption(int count) =>
        count == 1 ? "CLIP" : string.Create(CultureInfo.InvariantCulture, $"{count} CLIPS");

    private static string Key2(ImmutableArray<string> ids, string field) => InspectorEditor.CoalesceKey(ids, field);

    private static bool IsStill(Project project, MediaClip clip) =>
        TimelineQuery.FindMedia(project, clip.MediaId) is { Info.IsStill: true };

    private static MediaClip T(MediaClip m, Func<Transform, Transform> change) =>
        m with { Video = m.Video with { Transform = change(m.Video.Transform) } };

    private static MediaClip C(MediaClip m, Func<ColorAdjust, ColorAdjust> change) =>
        m with { Video = m.Video with { Color = change(m.Video.Color) } };

    private static MediaClip E(MediaClip m, Func<VideoEffects, VideoEffects> change) =>
        m with { Video = m.Video with { Effects = change(m.Video.Effects) } };

    private static MediaClip AE(MediaClip m, Func<AudioEffects, AudioEffects> change) =>
        m with { Audio = m.Audio with { Effects = change(m.Audio.Effects) } };

    private Func<Project, EditResult> UpdateVideo(string label, Func<MediaClip, MediaClip> change) =>
        p => TimelineEdits.UpdateClips(p, _videoIds, c => c is MediaClip m ? change(m) : c, label);

    private Func<Project, EditResult> UpdateFreeze(bool on) =>
        p => TimelineEdits.UpdateClips(p, _freezeIds, c => c is MediaClip m ? m with { Freeze = on } : c, on ? "Freeze Frame" : "Unfreeze");

    private Func<Project, EditResult> UpdateAudio(string label, Func<MediaClip, MediaClip> change) =>
        p => TimelineEdits.UpdateClips(p, _audioIds, c => c is MediaClip m ? change(m) : c, label);

    private void Video(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<VideoProps, double> read,
        Func<MediaClip, double, MediaClip> write,
        double defaultValue,
        double min,
        double max,
        double step,
        int decimals,
        string? unit)
    {
        var number = new InspectorNumberField(Editor, label, Key2(_videoIds, field), undoLabel, v => UpdateVideo(undoLabel, m => write(m, v)))
        {
            Read = c => c is MediaClip m ? read(m.Video) : null,
            DefaultValue = defaultValue,
            Minimum = min,
            Maximum = max,
            Step = step,
            Decimals = decimals,
            Unit = unit,
        };
        _videoFields.Add(group.Add(number));
    }

    private InspectorToggleRow VideoToggle(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<VideoProps, bool> read,
        Func<MediaClip, bool, MediaClip> write)
    {
        var row = group.Add(new InspectorToggleRow(Editor, label, Key2(_videoIds, field), on => UpdateVideo(undoLabel, m => write(m, on))));
        _videoToggles.Add((row, read));
        return row;
    }

    private void Audio(
        string label,
        string field,
        string undoLabel,
        Func<AudioProps, double> read,
        Func<MediaClip, double, MediaClip> write,
        double defaultValue,
        double min,
        double max,
        double step,
        int decimals,
        string? unit) =>
        Audio(_audioGroup, label, field, undoLabel, read, write, defaultValue, min, max, step, decimals, unit);

    private void Audio(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<AudioProps, double> read,
        Func<MediaClip, double, MediaClip> write,
        double defaultValue,
        double min,
        double max,
        double step,
        int decimals,
        string? unit)
    {
        var number = new InspectorNumberField(Editor, label, Key2(_audioIds, field), undoLabel, v => UpdateAudio(undoLabel, m => write(m, v)))
        {
            Read = c => c is MediaClip m ? read(m.Audio) : null,
            DefaultValue = defaultValue,
            Minimum = min,
            Maximum = max,
            Step = step,
            Decimals = decimals,
            Unit = unit,
        };
        _audioFields.Add(group.Add(number));
    }

    private InspectorToggleRow AudioToggle(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<AudioProps, bool> read,
        Func<MediaClip, bool, MediaClip> write)
    {
        var row = group.Add(new InspectorToggleRow(Editor, label, Key2(_audioIds, field), on => UpdateAudio(undoLabel, m => write(m, on))));
        _audioToggles.Add((row, read));
        return row;
    }

    private Func<Project, EditResult>? SourceInEdit(long frame) => frame < 0 ? null : p =>
        Chain(p, "Source In", _sourceIds, (q, id) =>
        {
            if (TimelineQuery.FindClip(q, id)?.Clip is not MediaClip m || m.Freeze)
            {
                return null;
            }

            var deltaSource = frame - SourceInFrame(m, q.Sequence.Settings.Rate);
            // Slip takes timeline frames at the clip's speed; the field shows source frames, so convert.
            var delta = (long)Math.Round(deltaSource * 100.0 / m.SpeedPercent, MidpointRounding.AwayFromZero);
            return delta == 0 ? null : TimelineEdits.Slip(q, id, delta, includeLinked: false);
        });

    private Func<Project, EditResult>? SourceOutEdit(long frame) => p =>
        Chain(p, "Source Out", _sourceIds, (q, id) =>
        {
            if (TimelineQuery.FindClip(q, id)?.Clip is not MediaClip m || m.Freeze)
            {
                return null;
            }

            var sourceFrames = frame - SourceInFrame(m, q.Sequence.Settings.Rate);
            var duration = Math.Max(1, (long)Math.Round(sourceFrames * 100.0 / m.SpeedPercent, MidpointRounding.AwayFromZero));
            return duration == m.Duration
                ? null
                : TimelineEdits.Trim(q, id, ClipEdge.End, duration - m.Duration, ripple: false, includeLinked: false);
        });

    private void RefreshKeyframes(MediaClip? clip, FrameRate rate)
    {
        var keys = clip?.Audio.Keys ?? ImmutableArray<VolumeKey>.Empty;
        if (keys.IsDefault)
        {
            keys = ImmutableArray<VolumeKey>.Empty;
        }

        if (_keyRows.Count != keys.Length)
        {
            foreach (var row in _keyRows)
            {
                _audioGroup.Rows.Remove(row);
            }

            _keyRows.Clear();
            for (var i = 0; i < keys.Length; i++)
            {
                var row = CreateKeyRow(i);
                _keyRows.Add(row);
                _audioGroup.Rows.Insert(_audioGroup.Rows.IndexOf(_addKeyRow), row);
            }
        }

        for (var i = 0; i < keys.Length; i++)
        {
            _keyRows[i].Time.Load(keys[i].Frame, rate);
            _keyRows[i].Level.Load(keys[i].Db);
        }
    }

    private InspectorKeyframeRow CreateKeyRow(int index)
    {
        var time = new InspectorTimecodeField(Editor, "Time", Key2(_audioIds, "audio.key" + index.ToString(CultureInfo.InvariantCulture) + ".time"), frame =>
            frame < 0 ? null : UpdateAudio("Move Keyframe", m => WithKey(m, index, k => k with { Frame = frame })));
        var level = new InspectorNumberField(
            Editor,
            "Level",
            Key2(_audioIds, "audio.key" + index.ToString(CultureInfo.InvariantCulture) + ".db"),
            "Keyframe Level",
            v => UpdateAudio("Keyframe Level", m => WithKey(m, index, k => k with { Db = v })))
        {
            Minimum = TimelineEdits.MinGainDb,
            Maximum = TimelineEdits.MaxGainDb,
            Step = 0.1,
            Decimals = 1,
            DefaultValue = 0,
            Unit = "dB",
        };
        var remove = new RelayCommand(() => Run(UpdateAudio("Remove Keyframe", m => m.Audio.Keys.Length > index
            ? m with { Audio = m.Audio with { Keys = m.Audio.Keys.RemoveAt(index) } }
            : m)));
        return new InspectorKeyframeRow(index, time, level, remove);
    }

    private static MediaClip WithKey(MediaClip m, int index, Func<VolumeKey, VolumeKey> change) =>
        m.Audio.Keys.IsDefault || index >= m.Audio.Keys.Length
            ? m
            : m with { Audio = m.Audio with { Keys = m.Audio.Keys.SetItem(index, change(m.Audio.Keys[index])) } };

    private void AddKeyframe()
    {
        if (_audioIds.Length != 1)
        {
            return;
        }

        var playhead = Editor.Timeline.Playhead;
        Run(p =>
        {
            if (TimelineQuery.FindClip(p, _audioIds[0])?.Clip is not MediaClip m)
            {
                return EditResult.Unchanged(p, "Add Keyframe");
            }

            // Gap filled: outside the clip the key goes to its first frame; a new key keeps the current envelope level.
            var frame = playhead >= m.Start && playhead < m.End ? playhead - m.Start : 0;
            var keys = m.Audio.Keys.IsDefault ? ImmutableArray<VolumeKey>.Empty : m.Audio.Keys;
            if (keys.Any(k => k.Frame == frame))
            {
                return EditResult.Unchanged(p, "Add Keyframe", "There is already a keyframe at that frame.");
            }

            var db = keys.IsEmpty ? 0 : TimelineEdits.EnvelopeAt(keys, frame);
            var added = keys.Add(new VolumeKey(frame, db)).Sort((a, b) => a.Frame.CompareTo(b.Frame));
            return TimelineEdits.UpdateClips(p, _audioIds, c => c is MediaClip mc ? mc with { Audio = mc.Audio with { Keys = added } } : c, "Add Keyframe");
        });
    }
}
