using System.Collections.Immutable;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// The inspector for media clips (DESIGN 4.4): Clip (name, file, enabled, start, duration, source in and out, speed),
/// Transform, Color, Fades and Audio. With several clips selected only fields they share show, and an edit applies to
/// all of them as one undo step: the video groups act on the selected video clips and the audio group on the selected
/// audio clips, so a linked video and audio pair shows both. Selected titles take part in the Clip rows only.
/// </summary>
public sealed partial class ClipSectionViewModel : InspectorClipSectionBase
{
    private readonly ImmutableArray<string> _videoIds;
    private readonly ImmutableArray<string> _audioIds;
    private readonly ImmutableArray<string> _sourceIds;
    private readonly List<InspectorNumberField> _videoFields = [];
    private readonly List<InspectorNumberField> _audioFields = [];
    private readonly InspectorGroup _clipGroup;
    private readonly InspectorGroup _transformGroup;
    private readonly InspectorGroup _colorGroup;
    private readonly InspectorGroup _fadesGroup;
    private readonly InspectorGroup _audioGroup;
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
        _sourceIds = [.. clips.OfType<MediaClip>().Where(m => TimelineQuery.IsBounded(m, TimelineQuery.FindMedia(project, m.MediaId))).Select(m => m.Id)];

        _clipGroup = AddGroup("CLIP");
        _clipGroup.Add(NameRow);
        _fileRow = _clipGroup.Add(new InspectorInfoRow("File"));
        _clipGroup.Add(EnabledRow);
        _clipGroup.Add(StartRow);
        _clipGroup.Add(DurationRow);
        _sourceInRow = _clipGroup.Add(new InspectorTimecodeField(editor, "Source in", Key2(_sourceIds, "clip.sourceIn"), SourceInEdit));
        _sourceOutRow = _clipGroup.Add(new InspectorTimecodeField(editor, "Source out", Key2(_sourceIds, "clip.sourceOut"), SourceOutEdit));
        _speedRow = _clipGroup.Add(new InspectorButtonRow("Speed", "100%", openSpeedDialog) { ToolTip = "Speed (Ctrl+R)" });

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

        _fadesGroup = AddGroup("FADES", () => Run(UpdateVideo("Reset Fades", m => m with { Video = m.Video with { FadeInFrames = 0, FadeOutFrames = 0 } })));
        Video(_fadesGroup, "Fade in", "video.fadeIn", "Fade", c => c.FadeInFrames, (m, v) => m with { Video = m.Video with { FadeInFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");
        Video(_fadesGroup, "Fade out", "video.fadeOut", "Fade", c => c.FadeOutFrames, (m, v) => m with { Video = m.Video with { FadeOutFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");

        _audioGroup = AddGroup("AUDIO", () => Run(UpdateAudio("Reset Audio", m => m with { Audio = AudioProps.Default })));
        Audio("Gain", "audio.gain", "Gain", a => a.GainDb, (m, v) => m with { Audio = m.Audio with { GainDb = v } }, 0, TimelineEdits.MinGainDb, TimelineEdits.MaxGainDb, 0.1, 1, "dB");
        Audio("Fade in", "audio.fadeIn", "Fade", a => a.FadeInFrames, (m, v) => m with { Audio = m.Audio with { FadeInFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");
        Audio("Fade out", "audio.fadeOut", "Fade", a => a.FadeOutFrames, (m, v) => m with { Audio = m.Audio with { FadeOutFrames = (long)v } }, 0, 0, 1_000_000, 0.25, 0, "f");
        _addKeyRow = _audioGroup.Add(new InspectorButtonRow("Keyframes", "Add at playhead", new RelayCommand(AddKeyframe))
        {
            ToolTip = "Adds a volume keyframe at the playhead (Ctrl+click on the volume line does the same)",
        });

        _transformGroup.IsVisible = !_videoIds.IsEmpty;
        _colorGroup.IsVisible = !_videoIds.IsEmpty;
        _fadesGroup.IsVisible = !_videoIds.IsEmpty;
        _audioGroup.IsVisible = !_audioIds.IsEmpty;
        _sourceInRow.IsVisible = !_sourceIds.IsEmpty;
        _sourceOutRow.IsVisible = !_sourceIds.IsEmpty;
        _speedRow.IsVisible = clips.OfType<MediaClip>().Any(m => !IsStill(project, m));
        _fileRow.IsVisible = clips.Length == 1 && clips[0] is MediaClip;
        _addKeyRow.IsVisible = _audioIds.Length == 1;
    }

    /// <summary>The selected video media clip ids (Transform, Color and Fades act on these).</summary>
    public ImmutableArray<string> VideoClipIds => _videoIds;

    /// <summary>The selected audio media clip ids (Audio acts on these).</summary>
    public ImmutableArray<string> AudioClipIds => _audioIds;

    /// <summary>Every numeric field of the video groups.</summary>
    public IReadOnlyList<InspectorNumberField> VideoFields => _videoFields;

    /// <summary>Every numeric field of the audio group (keyframe rows excluded).</summary>
    public IReadOnlyList<InspectorNumberField> AudioFields => _audioFields;

    /// <summary>The keyframe rows of a single selected audio clip.</summary>
    public IReadOnlyList<InspectorKeyframeRow> KeyframeRows => _keyRows;

    /// <summary>The speed button row.</summary>
    public InspectorButtonRow SpeedRow => _speedRow;

    /// <summary>The source in row.</summary>
    public InspectorTimecodeField SourceInRow => _sourceInRow;

    /// <summary>The source out row.</summary>
    public InspectorTimecodeField SourceOutRow => _sourceOutRow;

    /// <summary>The Transform group.</summary>
    public InspectorGroup TransformGroup => _transformGroup;

    /// <summary>The Color group.</summary>
    public InspectorGroup ColorGroup => _colorGroup;

    /// <summary>The video Fades group.</summary>
    public InspectorGroup FadesGroup => _fadesGroup;

    /// <summary>The Audio group.</summary>
    public InspectorGroup AudioGroup => _audioGroup;

    /// <summary>Finds a numeric field by label in the video or audio groups.</summary>
    /// <param name="label">The label, for example "Saturation".</param>
    /// <param name="audio">True to search the audio group.</param>
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

        var sources = clips.OfType<MediaClip>().Where(m => _sourceIds.Contains(m.Id)).ToList();
        var ins = sources.Select(m => SourceInFrame(m, rate)).Distinct().ToList();
        var outs = sources.Select(m => SourceOutFrame(m, rate)).Distinct().ToList();
        _sourceInRow.Load(ins.Count == 1 ? ins[0] : null, rate);
        _sourceOutRow.Load(outs.Count == 1 ? outs[0] : null, rate);
        var speeds = clips.OfType<MediaClip>().Where(m => !IsStill(project, m)).Select(m => m.SpeedPercent).Distinct().ToList();
        _speedRow.ButtonText = speeds.Count == 1 ? speeds[0].ToString(CultureInfo.InvariantCulture) + "%" : "Mixed";

        var videos = clips.Where(c => _videoIds.Contains(c.Id)).ToList();
        foreach (var field in _videoFields)
        {
            field.LoadFrom(videos);
        }

        var audios = clips.Where(c => _audioIds.Contains(c.Id)).ToList();
        foreach (var field in _audioFields)
        {
            field.LoadFrom(audios);
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

    private Func<Project, EditResult> UpdateVideo(string label, Func<MediaClip, MediaClip> change) =>
        p => TimelineEdits.UpdateClips(p, _videoIds, c => c is MediaClip m ? change(m) : c, label);

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
        _audioFields.Add(_audioGroup.Add(number));
    }

    private Func<Project, EditResult>? SourceInEdit(long frame) => frame < 0 ? null : p =>
        Chain(p, "Source In", _sourceIds, (q, id) =>
        {
            if (TimelineQuery.FindClip(q, id)?.Clip is not MediaClip m)
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
            if (TimelineQuery.FindClip(q, id)?.Clip is not MediaClip m)
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
