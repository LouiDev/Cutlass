using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// The video and audio property groups shared by the media clip and the generator sections (DESIGN 4.4): Transform,
/// Color, Effects, Chroma Key and Fades act on the selected clips with video props (video media clips, colour mattes
/// and bars); Audio (gain, fades, keyframes) and Audio Effects on the clips with audio props (audio media clips and
/// tones). A derived section adds its own leading groups, then calls <see cref="AddPropsGroups"/>, and reloads with
/// <see cref="RefreshProps"/>. Effects batch 5 moved these groups here from the media clip section so generators reuse
/// every row, coalesce key and Reset.
/// </summary>
public abstract class InspectorPropsSectionBase : InspectorClipSectionBase
{
    private readonly List<InspectorNumberField> _videoFields = [];
    private readonly List<InspectorNumberField> _audioFields = [];
    private readonly List<(InspectorToggleRow Row, Func<VideoProps, bool> Read)> _videoToggles = [];
    private readonly List<(InspectorToggleRow Row, Func<AudioProps, bool> Read)> _audioToggles = [];
    private readonly List<InspectorKeyframeRow> _keyRows = [];
    private ImmutableArray<string> _videoIds = [];
    private ImmutableArray<string> _audioIds = [];
    private InspectorGroup _transformGroup = null!;
    private InspectorGroup _colorGroup = null!;
    private InspectorGroup _effectsGroup = null!;
    private InspectorGroup _keyGroup = null!;
    private InspectorColorField _keyColorRow = null!;
    private InspectorGroup _fadesGroup = null!;
    private InspectorGroup _audioGroup = null!;
    private InspectorGroup _audioFxGroup = null!;
    private InspectorButtonRow _addKeyRow = null!;

    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="key">The selection identity.</param>
    /// <param name="header">The header.</param>
    /// <param name="clipIds">The selected clip ids.</param>
    protected InspectorPropsSectionBase(
        InspectorEditor editor, IDictionary<string, bool> states, string key, string header, IReadOnlyList<string> clipIds)
        : base(editor, states, key, header, clipIds)
    {
    }

    /// <summary>The selected clips with video props (Transform, Color, Effects, Chroma Key and Fades act on these).</summary>
    public ImmutableArray<string> VideoClipIds => _videoIds;

    /// <summary>The selected clips with audio props (Audio and Audio Effects act on these).</summary>
    public ImmutableArray<string> AudioClipIds => _audioIds;

    /// <summary>Every numeric field of the video groups.</summary>
    public IReadOnlyList<InspectorNumberField> VideoFields => _videoFields;

    /// <summary>Every numeric field of the audio groups (keyframe rows excluded).</summary>
    public IReadOnlyList<InspectorNumberField> AudioFields => _audioFields;

    /// <summary>Every toggle row of the Audio Effects group (compressor, gate, mono).</summary>
    public IReadOnlyList<InspectorToggleRow> AudioToggles => [.. _audioToggles.Select(t => t.Row)];

    /// <summary>The Compressor toggle.</summary>
    public InspectorToggleRow CompressorRow { get; private set; } = null!;

    /// <summary>The Gate toggle.</summary>
    public InspectorToggleRow GateRow { get; private set; } = null!;

    /// <summary>The Mono toggle.</summary>
    public InspectorToggleRow MonoRow { get; private set; } = null!;

    /// <summary>Every toggle row of the video groups (flips, grayscale, chroma key enabled).</summary>
    public IReadOnlyList<InspectorToggleRow> VideoToggles => [.. _videoToggles.Select(t => t.Row)];

    /// <summary>The Flip horizontal toggle.</summary>
    public InspectorToggleRow FlipHRow { get; private set; } = null!;

    /// <summary>The Flip vertical toggle.</summary>
    public InspectorToggleRow FlipVRow { get; private set; } = null!;

    /// <summary>The Grayscale toggle.</summary>
    public InspectorToggleRow GrayscaleRow { get; private set; } = null!;

    /// <summary>The chroma key Enabled toggle.</summary>
    public InspectorToggleRow KeyEnabledRow { get; private set; } = null!;

    /// <summary>The chroma key colour row.</summary>
    public InspectorColorField KeyColorRow => _keyColorRow;

    /// <summary>The keyframe rows of a single selected clip with audio props.</summary>
    public IReadOnlyList<InspectorKeyframeRow> KeyframeRows => _keyRows;

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

    /// <summary>The video props of a clip that has them: a video media clip, a colour matte or bars.</summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The props, or null.</returns>
    public static VideoProps? VideoOf(Clip clip) => clip switch
    {
        MediaClip { Stream: StreamKind.Video } m => m.Video,
        GeneratorClip g when g.Kind.IsVideo() => g.Video,
        _ => null,
    };

    /// <summary>The audio props of a clip that has them: an audio media clip or a tone.</summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The props, or null.</returns>
    public static AudioProps? AudioOf(Clip clip) => ClipAudio.Of(clip);

    /// <summary>Adds Transform, Color, Effects, Chroma Key, Fades, Audio and Audio Effects, in that order.</summary>
    /// <param name="videoIds">The selected clips with video props.</param>
    /// <param name="audioIds">The selected clips with audio props.</param>
    protected void AddPropsGroups(ImmutableArray<string> videoIds, ImmutableArray<string> audioIds)
    {
        _videoIds = videoIds;
        _audioIds = audioIds;
        _transformGroup = AddGroup("TRANSFORM", () => Run(UpdateVideo("Reset Transform", v => v with { Transform = Transform.Identity, Opacity = 1 })));
        Video(_transformGroup, "Position X", "video.transform.x", "Position", c => c.Transform.X, (p, v) => T(p, t => t with { X = v }), 0, -100_000, 100_000, 1, 0, "px");
        Video(_transformGroup, "Position Y", "video.transform.y", "Position", c => c.Transform.Y, (p, v) => T(p, t => t with { Y = v }), 0, -100_000, 100_000, 1, 0, "px");
        Video(_transformGroup, "Scale", "video.transform.scale", "Scale", c => c.Transform.Scale * 100, (p, v) => T(p, t => t with { Scale = v / 100 }), 100, 1, 10_000, 0.5, 1, "%");
        Video(_transformGroup, "Crop left", "video.transform.cropLeft", "Crop", c => c.Transform.CropLeft * 100, (p, v) => T(p, t => t with { CropLeft = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Crop top", "video.transform.cropTop", "Crop", c => c.Transform.CropTop * 100, (p, v) => T(p, t => t with { CropTop = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Crop right", "video.transform.cropRight", "Crop", c => c.Transform.CropRight * 100, (p, v) => T(p, t => t with { CropRight = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Crop bottom", "video.transform.cropBottom", "Crop", c => c.Transform.CropBottom * 100, (p, v) => T(p, t => t with { CropBottom = v / 100 }), 0, 0, 90, 0.2, 1, "%");
        Video(_transformGroup, "Opacity", "video.opacity", "Opacity", c => c.Opacity * 100, (p, v) => p with { Opacity = v / 100 }, 100, 0, 100, 0.5, 0, "%");

        _colorGroup = AddGroup("COLOR", () => Run(UpdateVideo("Reset Color", v => v with { Color = ColorAdjust.Neutral })));
        Video(_colorGroup, "Brightness", "color.brightness", "Brightness", c => c.Color.Brightness, (p, v) => C(p, a => a with { Brightness = v }), 0, -1, 1, 0.005, 2, null);
        Video(_colorGroup, "Contrast", "color.contrast", "Contrast", c => c.Color.Contrast, (p, v) => C(p, a => a with { Contrast = v }), 1, 0, 2, 0.005, 2, null);
        Video(_colorGroup, "Saturation", "color.saturation", "Saturation", c => c.Color.Saturation, (p, v) => C(p, a => a with { Saturation = v }), 1, 0, 3, 0.01, 2, null);
        Video(_colorGroup, "Gamma", "color.gamma", "Gamma", c => c.Color.Gamma, (p, v) => C(p, a => a with { Gamma = v }), 1, 0.1, 4, 0.01, 2, null);

        // Effects batch 2 (DESIGN 4.4): the static effects and the chroma key, each with its own Reset. Percent rows
        // convert like Opacity; the model keeps 0..1 (or -1..1 for the tint).
        _effectsGroup = AddGroup("EFFECTS", () => Run(UpdateVideo("Reset Effects", v => v with { Effects = VideoEffects.None })));
        FlipHRow = VideoToggle(_effectsGroup, "Flip horizontal", "video.effects.flipH", "Flip Horizontal", c => c.Effects.FlipH, (p, on) => E(p, e => e with { FlipH = on }));
        FlipVRow = VideoToggle(_effectsGroup, "Flip vertical", "video.effects.flipV", "Flip Vertical", c => c.Effects.FlipV, (p, on) => E(p, e => e with { FlipV = on }));
        Video(_effectsGroup, "Rotation", "video.effects.rotation", "Rotation", c => c.Effects.RotationDegrees, (p, v) => E(p, e => e with { RotationDegrees = v }), 0, -180, 180, 0.5, 1, "°");
        Video(_effectsGroup, "Hue", "video.effects.hue", "Hue", c => c.Effects.HueDegrees, (p, v) => E(p, e => e with { HueDegrees = v }), 0, -180, 180, 0.5, 1, "°");
        Video(_effectsGroup, "Temperature", "video.effects.temperature", "Temperature", c => c.Effects.TemperatureKelvin, (p, v) => E(p, e => e with { TemperatureKelvin = v }), 6500, 1000, 40000, 50, 0, "K");
        Video(_effectsGroup, "Tint", "video.effects.tint", "Tint", c => c.Effects.Tint * 100, (p, v) => E(p, e => e with { Tint = v / 100 }), 0, -100, 100, 0.5, 0, "%");
        Video(_effectsGroup, "Blur", "video.effects.blur", "Blur", c => c.Effects.Blur, (p, v) => E(p, e => e with { Blur = v }), 0, 0, 50, 0.1, 1, "px");
        Video(_effectsGroup, "Sharpen", "video.effects.sharpen", "Sharpen", c => c.Effects.Sharpen * 100, (p, v) => E(p, e => e with { Sharpen = v / 100 }), 0, 0, 100, 0.5, 0, "%");
        Video(_effectsGroup, "Vignette", "video.effects.vignette", "Vignette", c => c.Effects.Vignette * 100, (p, v) => E(p, e => e with { Vignette = v / 100 }), 0, 0, 100, 0.5, 0, "%");
        GrayscaleRow = VideoToggle(_effectsGroup, "Grayscale", "video.effects.grayscale", "Grayscale", c => c.Effects.Grayscale, (p, on) => E(p, e => e with { Grayscale = on }));

        _keyGroup = AddGroup("CHROMA KEY", () => Run(UpdateVideo("Reset Chroma Key", v => E(v, e => e with
        {
            KeyEnabled = VideoEffects.None.KeyEnabled,
            KeyColor = VideoEffects.None.KeyColor,
            KeySimilarity = VideoEffects.None.KeySimilarity,
            KeyBlend = VideoEffects.None.KeyBlend,
        }))));
        KeyEnabledRow = VideoToggle(_keyGroup, "Key enabled", "video.effects.keyEnabled", "Chroma Key", c => c.Effects.KeyEnabled, (p, on) => E(p, e => e with { KeyEnabled = on }));
        // The colour field normalizes to #RRGGBBAA; the key colour is opaque, so the model keeps the #RRGGBB part.
        _keyColorRow = _keyGroup.Add(new InspectorColorField(Editor, "Key color", Key2(_videoIds, "video.effects.keyColor"), c => UpdateVideo("Key Color", v => E(v, e => e with { KeyColor = c[..7] }))));
        Video(_keyGroup, "Similarity", "video.effects.keySimilarity", "Key Similarity", c => c.Effects.KeySimilarity * 100, (p, v) => E(p, e => e with { KeySimilarity = v / 100 }), 10, 1, 100, 0.5, 0, "%");
        Video(_keyGroup, "Blend", "video.effects.keyBlend", "Key Blend", c => c.Effects.KeyBlend * 100, (p, v) => E(p, e => e with { KeyBlend = v / 100 }), 0, 0, 100, 0.5, 0, "%");

        _fadesGroup = AddGroup("FADES", () => Run(UpdateVideo("Reset Fades", v => v with { FadeInFrames = 0, FadeOutFrames = 0 })));
        Video(_fadesGroup, "Fade in", "video.fadeIn", "Fade", c => c.FadeInFrames, (p, v) => p with { FadeInFrames = (long)v }, 0, 0, 1_000_000, 0.25, 0, "f");
        Video(_fadesGroup, "Fade out", "video.fadeOut", "Fade", c => c.FadeOutFrames, (p, v) => p with { FadeOutFrames = (long)v }, 0, 0, 1_000_000, 0.25, 0, "f");

        // The AUDIO EFFECTS group has its own Reset, so this one keeps the effects (gain, fades and keyframes reset).
        _audioGroup = AddGroup("AUDIO", () => Run(UpdateAudio("Reset Audio", a => AudioProps.Default with { Effects = a.Effects })));
        Audio(_audioGroup, "Gain", "audio.gain", "Gain", a => a.GainDb, (p, v) => p with { GainDb = v }, 0, TimelineEdits.MinGainDb, TimelineEdits.MaxGainDb, 0.1, 1, "dB");
        Audio(_audioGroup, "Fade in", "audio.fadeIn", "Fade", a => a.FadeInFrames, (p, v) => p with { FadeInFrames = (long)v }, 0, 0, 1_000_000, 0.25, 0, "f");
        Audio(_audioGroup, "Fade out", "audio.fadeOut", "Fade", a => a.FadeOutFrames, (p, v) => p with { FadeOutFrames = (long)v }, 0, 0, 1_000_000, 0.25, 0, "f");
        _addKeyRow = _audioGroup.Add(new InspectorButtonRow("Keyframes", "Add at playhead", new RelayCommand(AddKeyframe))
        {
            ToolTip = "Adds a volume keyframe at the playhead (Ctrl+click on the volume line does the same)",
        });

        // Effects batch 3 (DESIGN 4.4): the static audio effects in their own group, after AUDIO so the keyframe button
        // stays the last AUDIO row. Thresholds are dBFS, the pan row is percent over the model's -1..1.
        _audioFxGroup = AddGroup("AUDIO EFFECTS", () => Run(UpdateAudio("Reset Audio Effects", a => a with { Effects = AudioEffects.None })));
        Audio(_audioFxGroup, "High-pass", "audio.effects.highPass", "High-pass", a => a.Effects.HighPassHz, (p, v) => AE(p, e => e with { HighPassHz = v }), 0, 0, 2000, 10, 0, "Hz");
        Audio(_audioFxGroup, "Low-pass", "audio.effects.lowPass", "Low-pass", a => a.Effects.LowPassHz, (p, v) => AE(p, e => e with { LowPassHz = v }), 0, 0, 20000, 100, 0, "Hz");
        Audio(_audioFxGroup, "Bass", "audio.effects.bass", "Bass", a => a.Effects.BassDb, (p, v) => AE(p, e => e with { BassDb = v }), 0, -20, 20, 0.1, 1, "dB");
        Audio(_audioFxGroup, "Treble", "audio.effects.treble", "Treble", a => a.Effects.TrebleDb, (p, v) => AE(p, e => e with { TrebleDb = v }), 0, -20, 20, 0.1, 1, "dB");
        CompressorRow = AudioToggle(_audioFxGroup, "Compressor", "audio.effects.compressor", "Compressor", a => a.Effects.Compressor, (p, on) => AE(p, e => e with { Compressor = on }));
        Audio(_audioFxGroup, "Threshold", "audio.effects.compressorThreshold", "Compressor Threshold", a => a.Effects.CompressorThresholdDb, (p, v) => AE(p, e => e with { CompressorThresholdDb = v }), -20, -60, 0, 0.5, 1, "dB");
        Audio(_audioFxGroup, "Ratio", "audio.effects.compressorRatio", "Compressor Ratio", a => a.Effects.CompressorRatio, (p, v) => AE(p, e => e with { CompressorRatio = v }), 4, 1, 20, 0.1, 1, ":1");
        GateRow = AudioToggle(_audioFxGroup, "Gate", "audio.effects.gate", "Gate", a => a.Effects.Gate, (p, on) => AE(p, e => e with { Gate = on }));
        Audio(_audioFxGroup, "Gate threshold", "audio.effects.gateThreshold", "Gate Threshold", a => a.Effects.GateThresholdDb, (p, v) => AE(p, e => e with { GateThresholdDb = v }), -40, -80, 0, 0.5, 1, "dB");
        Audio(_audioFxGroup, "Pan", "audio.effects.pan", "Pan", a => a.Effects.Pan * 100, (p, v) => AE(p, e => e with { Pan = v / 100 }), 0, -100, 100, 1, 0, "%");
        MonoRow = AudioToggle(_audioFxGroup, "Mono", "audio.effects.mono", "Mono", a => a.Effects.Mono, (p, on) => AE(p, e => e with { Mono = on }));

        _transformGroup.IsVisible = !_videoIds.IsEmpty;
        _colorGroup.IsVisible = !_videoIds.IsEmpty;
        _effectsGroup.IsVisible = !_videoIds.IsEmpty;
        _keyGroup.IsVisible = !_videoIds.IsEmpty;
        _fadesGroup.IsVisible = !_videoIds.IsEmpty;
        _audioGroup.IsVisible = !_audioIds.IsEmpty;
        _audioFxGroup.IsVisible = !_audioIds.IsEmpty;
        _addKeyRow.IsVisible = _audioIds.Length == 1;
    }

    /// <summary>Reloads every row of the property groups from the selected clips.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clips">The selected clips.</param>
    protected void RefreshProps(Project project, IReadOnlyList<Clip> clips)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(clips);
        var videos = clips.Where(c => _videoIds.Contains(c.Id)).ToList();
        foreach (var field in _videoFields)
        {
            field.LoadFrom(videos);
        }

        // Toggle and colour rows have no Read like the number fields, so they load here (as the title section does).
        var videoProps = videos.Select(VideoOf).OfType<VideoProps>().ToList();
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

        var audioProps = audios.Select(AudioOf).OfType<AudioProps>().ToList();
        foreach (var (row, read) in _audioToggles)
        {
            row.LoadMany(audioProps.Select(read));
        }

        RefreshKeyframes(audioProps.Count == 1 ? audioProps[0] : null, project.Sequence.Settings.Rate);
    }

    /// <summary>The coalesce key of a field over some ids.</summary>
    /// <param name="ids">The ids.</param>
    /// <param name="field">The field.</param>
    /// <returns>The key.</returns>
    protected static string Key2(ImmutableArray<string> ids, string field) => InspectorEditor.CoalesceKey(ids, field);

    /// <summary>An edit of the video props of the selected clips that have them.</summary>
    /// <param name="label">The undo label.</param>
    /// <param name="change">The change.</param>
    /// <returns>The edit.</returns>
    protected Func<Project, EditResult> UpdateVideo(string label, Func<VideoProps, VideoProps> change) =>
        p => TimelineEdits.UpdateClips(p, _videoIds, c => VideoOf(c) is { } v ? WithVideo(c, change(v)) : c, label);

    /// <summary>An edit of the audio props of the selected clips that have them.</summary>
    /// <param name="label">The undo label.</param>
    /// <param name="change">The change.</param>
    /// <returns>The edit.</returns>
    protected Func<Project, EditResult> UpdateAudio(string label, Func<AudioProps, AudioProps> change) =>
        p => TimelineEdits.UpdateClips(p, _audioIds, c => AudioOf(c) is { } a ? WithAudio(c, change(a)) : c, label);

    private static Clip WithVideo(Clip clip, VideoProps video) => clip switch
    {
        MediaClip m => m with { Video = video },
        GeneratorClip g => g with { Video = video },
        _ => clip,
    };

    private static Clip WithAudio(Clip clip, AudioProps audio) => ClipAudio.With(clip, audio);

    private static VideoProps T(VideoProps p, Func<Transform, Transform> change) => p with { Transform = change(p.Transform) };

    private static VideoProps C(VideoProps p, Func<ColorAdjust, ColorAdjust> change) => p with { Color = change(p.Color) };

    private static VideoProps E(VideoProps p, Func<VideoEffects, VideoEffects> change) => p with { Effects = change(p.Effects) };

    private static AudioProps AE(AudioProps p, Func<AudioEffects, AudioEffects> change) => p with { Effects = change(p.Effects) };

    private void Video(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<VideoProps, double> read,
        Func<VideoProps, double, VideoProps> write,
        double defaultValue,
        double min,
        double max,
        double step,
        int decimals,
        string? unit)
    {
        var number = new InspectorNumberField(Editor, label, Key2(_videoIds, field), undoLabel, v => UpdateVideo(undoLabel, p => write(p, v)))
        {
            Read = c => VideoOf(c) is { } p ? read(p) : null,
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
        Func<VideoProps, bool, VideoProps> write)
    {
        var row = group.Add(new InspectorToggleRow(Editor, label, Key2(_videoIds, field), on => UpdateVideo(undoLabel, p => write(p, on))));
        _videoToggles.Add((row, read));
        return row;
    }

    private void Audio(
        InspectorGroup group,
        string label,
        string field,
        string undoLabel,
        Func<AudioProps, double> read,
        Func<AudioProps, double, AudioProps> write,
        double defaultValue,
        double min,
        double max,
        double step,
        int decimals,
        string? unit)
    {
        var number = new InspectorNumberField(Editor, label, Key2(_audioIds, field), undoLabel, v => UpdateAudio(undoLabel, p => write(p, v)))
        {
            Read = c => AudioOf(c) is { } p ? read(p) : null,
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
        Func<AudioProps, bool, AudioProps> write)
    {
        var row = group.Add(new InspectorToggleRow(Editor, label, Key2(_audioIds, field), on => UpdateAudio(undoLabel, p => write(p, on))));
        _audioToggles.Add((row, read));
        return row;
    }

    private void RefreshKeyframes(AudioProps? audio, FrameRate rate)
    {
        var keys = audio?.Keys ?? ImmutableArray<VolumeKey>.Empty;
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
            frame < 0 ? null : UpdateAudio("Move Keyframe", a => WithKey(a, index, k => k with { Frame = frame })));
        var level = new InspectorNumberField(
            Editor,
            "Level",
            Key2(_audioIds, "audio.key" + index.ToString(CultureInfo.InvariantCulture) + ".db"),
            "Keyframe Level",
            v => UpdateAudio("Keyframe Level", a => WithKey(a, index, k => k with { Db = v })))
        {
            Minimum = TimelineEdits.MinGainDb,
            Maximum = TimelineEdits.MaxGainDb,
            Step = 0.1,
            Decimals = 1,
            DefaultValue = 0,
            Unit = "dB",
        };
        var remove = new RelayCommand(() => Run(UpdateAudio("Remove Keyframe", a => a.Keys.Length > index
            ? a with { Keys = a.Keys.RemoveAt(index) }
            : a)));
        return new InspectorKeyframeRow(index, time, level, remove);
    }

    private static AudioProps WithKey(AudioProps a, int index, Func<VolumeKey, VolumeKey> change) =>
        a.Keys.IsDefault || index >= a.Keys.Length
            ? a
            : a with { Keys = a.Keys.SetItem(index, change(a.Keys[index])) };

    private void AddKeyframe()
    {
        if (_audioIds.Length != 1)
        {
            return;
        }

        var playhead = Editor.Timeline.Playhead;
        Run(p =>
        {
            if (TimelineQuery.FindClip(p, _audioIds[0])?.Clip is not { } clip || AudioOf(clip) is not { } audio)
            {
                return EditResult.Unchanged(p, "Add Keyframe");
            }

            // Gap filled: outside the clip the key goes to its first frame; a new key keeps the current envelope level.
            var frame = playhead >= clip.Start && playhead < clip.End ? playhead - clip.Start : 0;
            var keys = audio.Keys.IsDefault ? ImmutableArray<VolumeKey>.Empty : audio.Keys;
            if (keys.Any(k => k.Frame == frame))
            {
                return EditResult.Unchanged(p, "Add Keyframe", "There is already a keyframe at that frame.");
            }

            var db = keys.IsEmpty ? 0 : TimelineEdits.EnvelopeAt(keys, frame);
            var added = keys.Add(new VolumeKey(frame, db)).Sort((a, b) => a.Frame.CompareTo(b.Frame));
            return TimelineEdits.UpdateClips(p, _audioIds, c => AudioOf(c) is { } a ? WithAudio(c, a with { Keys = added }) : c, "Add Keyframe");
        });
    }
}
