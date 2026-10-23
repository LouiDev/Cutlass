using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.ViewModels.Timeline;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The Effects tab of the left panel (DESIGN 4.2, 5.5): the eight video transitions plus Audio Crossfade, the title
/// templates Centered, Lower Third and Top Caption, and fade presets. Apply acts on the timeline selection:
/// <list type="bullet">
/// <item>A transition goes to the selected transition (its kind changes), else to the cuts between adjacent selected
/// clips, else to every cut of the selected clips, else to the cut nearest the playhead on the targeted tracks. Video
/// kinds use video tracks and Audio Crossfade audio tracks; the duration is 1 s, centred on the cut.</item>
/// <item>A title template places a 5 s title at the playhead on the lowest unlocked video track above every clip under
/// the playhead that is free for those 5 s; without one a new video track is added on top (or, at 8 video tracks, the
/// topmost unlocked track is overwritten).</item>
/// <item>A fade preset sets 1 s video and audio fades on the selected clips.</item>
/// </list>
/// Double-clicking a video transition makes it the default (star) used by Ctrl+D (DESIGN 4.2); double-clicking any other
/// item applies it.
/// </summary>
public sealed partial class EffectsViewModel : ObservableObject
{
    /// <summary>The default transition length in seconds (DESIGN 5.5).</summary>
    public const double TransitionSeconds = 1;

    /// <summary>The length of a new title in seconds (DESIGN 4.2).</summary>
    public const double TitleSeconds = 5;

    /// <summary>The length of the fade presets in seconds.</summary>
    public const double FadeSeconds = 1;

    /// <summary>The length of new transitions in seconds (Settings, "Default transition"; starts at <see cref="TransitionSeconds"/>).</summary>
    public double TransitionLengthSeconds { get; set; } = TransitionSeconds;

    /// <summary>The length of new titles in seconds (Settings, "Default title duration"; starts at <see cref="TitleSeconds"/>).</summary>
    public double TitleLengthSeconds { get; set; } = TitleSeconds;

    private readonly TimelineViewModel _timeline;

    /// <summary>Creates the tab.</summary>
    /// <param name="timeline">The timeline (session, selection, playhead, ids).</param>
    public EffectsViewModel(TimelineViewModel timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        _timeline = timeline;
        foreach (var kind in Enum.GetValues<TransitionKind>())
        {
            Transitions.Add(new EffectItem(
                EffectItem.TransitionPrefix + kind,
                EffectItem.TransitionName(kind),
                EffectCategory.Transition,
                EffectItem.TransitionBadge(kind),
                "1 s",
                kind == TransitionKind.AudioCrossfade
                    ? "Audio crossfade on the selected audio cuts (Ctrl+Shift+D)."
                    : "Video transition on the selected cuts. Double-click to make it the default (Ctrl+D).")
            {
                TransitionKind = kind,
            });
        }

        Titles.Add(TitleItem("Centered", "Centered", "Large centred title.", TitleSpec.Default with { Text = "Title", Anchor = TitleAnchor.Center }));
        Titles.Add(TitleItem("LowerThird", "Lower Third", "Name strap in the lower third.", TitleSpec.Default with
        {
            Text = "Name",
            Font = "builtin:Inter-SemiBold",
            Size = 64,
            Box = true,
            Anchor = TitleAnchor.LowerThird,
        }));
        Titles.Add(TitleItem("TopCaption", "Top Caption", "Small caption near the top.", TitleSpec.Default with
        {
            Text = "Caption",
            Font = "builtin:Inter-Regular",
            Size = 48,
            OutlineWidth = 0,
            Box = true,
            Anchor = TitleAnchor.Top,
        }));

        Adjustments.Add(new EffectItem(EffectItem.AdjustmentPrefix + "FadeIn", "Fade In", EffectCategory.Adjustment, "F↗", "1 s", "1 s video and audio fade in on the selected clips."));
        Adjustments.Add(new EffectItem(EffectItem.AdjustmentPrefix + "FadeOut", "Fade Out", EffectCategory.Adjustment, "F↘", "1 s", "1 s video and audio fade out on the selected clips."));
        Adjustments.Add(new EffectItem(EffectItem.AdjustmentPrefix + "FadeInOut", "Fade In and Out", EffectCategory.Adjustment, "F↕", "1 s", "1 s fades at both ends of the selected clips."));
        Adjustments.Add(new EffectItem(EffectItem.AdjustmentPrefix + "RemoveFades", "Remove Fades", EffectCategory.Adjustment, "F0", string.Empty, "Removes the fades of the selected clips."));

        DefaultTransitionKind = TransitionKind.Crossfade;
        MarkDefault(DefaultTransitionKind);
        StatusText = string.Empty;
    }

    /// <summary>Raised when the default transition changes (the shell may persist it in settings).</summary>
    public event EventHandler? DefaultTransitionChanged;

    /// <summary>The transitions.</summary>
    public ObservableCollection<EffectItem> Transitions { get; } = [];

    /// <summary>The title templates.</summary>
    public ObservableCollection<EffectItem> Titles { get; } = [];

    /// <summary>The adjustments (fade presets).</summary>
    public ObservableCollection<EffectItem> Adjustments { get; } = [];

    /// <summary>Every item.</summary>
    public IEnumerable<EffectItem> All => Transitions.Concat(Titles).Concat(Adjustments);

    /// <summary>The default video transition (Ctrl+D). Crossfade is the factory default.</summary>
    [ObservableProperty]
    public partial TransitionKind DefaultTransitionKind { get; set; }

    /// <summary>The selected item, which the Apply button applies.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplySelectedCommand))]
    public partial EffectItem? SelectedItem { get; set; }

    /// <summary>The outcome of the last apply, for example "Added 2 transitions" or why nothing happened.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; }

    /// <summary>
    /// True while an agent holds or pauses the project (AGENT_INTEGRATION 3.7): Apply, double-click apply and drags to
    /// the timeline are disabled. Choosing the default transition stays (it is a user setting, not a project edit).
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyItemCommand), nameof(ApplySelectedCommand))]
    public partial bool IsReadOnly { get; set; }

    /// <summary>The status shown when an apply is refused because an agent holds the project.</summary>
    public const string ReadOnlyStatus = "An agent is editing. Take over to apply effects.";

    /// <summary>The frames of a duration in seconds at a rate, at least 1.</summary>
    /// <param name="seconds">The seconds.</param>
    /// <param name="rate">The rate.</param>
    /// <returns>The frames, rounded.</returns>
    public static long FramesFor(double seconds, FrameRate rate) =>
        Math.Max(1, (long)Math.Round(seconds * rate.Value.Num / rate.Value.Den, MidpointRounding.AwayFromZero));

    /// <summary>
    /// The default transition stored in settings (a <see cref="TransitionKind"/> name). Unknown names and Audio
    /// Crossfade (never a Ctrl+D default) give the factory default, Crossfade.
    /// </summary>
    /// <param name="name">The stored name, or null.</param>
    /// <returns>The video transition kind.</returns>
    public static TransitionKind ParseDefaultTransition(string? name) =>
        Enum.TryParse<TransitionKind>(name, ignoreCase: true, out var kind) && Enum.IsDefined(kind)
            && kind != TransitionKind.AudioCrossfade && !int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            ? kind
            : TransitionKind.Crossfade;

    /// <summary>Finds an item by id.</summary>
    /// <param name="effectId">The id.</param>
    /// <returns>The item, or null.</returns>
    public EffectItem? Find(string? effectId) => All.FirstOrDefault(i => i.Id == effectId);

    /// <summary>
    /// The cuts an Apply of a transition acts on (see the class remarks), as track and cut frame pairs.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="audio">True for audio tracks (Audio Crossfade), false for video tracks.</param>
    /// <returns>The cuts.</returns>
    public ImmutableArray<(string TrackId, long CutFrame)> CutsForSelection(Project project, bool audio)
    {
        ArgumentNullException.ThrowIfNull(project);
        var kind = audio ? TrackKind.Audio : TrackKind.Video;
        var tracks = project.Sequence.Tracks.Where(t => t.Kind == kind && !t.Locked).ToList();
        var selected = _timeline.SelectedClipIds;
        if (selected.Count > 0)
        {
            var between = new List<(string, long)>();
            var touching = new List<(string, long)>();
            foreach (var track in tracks)
            {
                for (var i = 0; i + 1 < track.Clips.Length; i++)
                {
                    var left = track.Clips[i];
                    var right = track.Clips[i + 1];
                    if (left.End != right.Start)
                    {
                        continue;
                    }

                    var l = selected.Contains(left.Id);
                    var r = selected.Contains(right.Id);
                    if (l && r)
                    {
                        between.Add((track.Id, left.End));
                    }
                    else if (l || r)
                    {
                        touching.Add((track.Id, left.End));
                    }
                }
            }

            // Gap filled: DESIGN says "the selected clips' cuts"; when adjacent selected clips exist only the cuts between
            // them count, so selecting two clips puts one transition between them.
            return [.. between.Count > 0 ? between : touching];
        }

        var playhead = _timeline.Playhead;
        var targeted = tracks.Where(t => t.Targeted).ToList();
        foreach (var candidates in new[] { targeted, tracks })
        {
            var cuts = candidates.SelectMany(t => Cuts(t).Select(c => (t.Id, Cut: c))).ToList();
            if (cuts.Count == 0)
            {
                continue;
            }

            var nearest = cuts.MinBy(c => Math.Abs(c.Cut - playhead)).Cut;
            return [.. cuts.Where(c => c.Cut == nearest)];
        }

        return [];
    }

    /// <summary>Applies an effect to the current selection (see the class remarks).</summary>
    /// <param name="item">The effect.</param>
    /// <returns>The edit result, or null when nothing applied (see <see cref="StatusText"/>).</returns>
    public EditResult? Apply(EffectItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Category switch
        {
            EffectCategory.Transition when item.TransitionKind is { } kind => ApplyTransition(kind),
            EffectCategory.Title when item.Title is { } spec => AddTitle(spec, _timeline.Playhead, null),
            EffectCategory.Adjustment => ApplyAdjustment(item.Id),
            _ => null,
        };
    }

    /// <summary>
    /// Applies an effect at a timeline position, for drops: a transition on the cut of <paramref name="trackId"/> nearest
    /// <paramref name="frame"/>, a title placed at <paramref name="frame"/> on that video track, an adjustment on the
    /// clip under <paramref name="frame"/> (and its linked partners while linked selection is on).
    /// </summary>
    /// <param name="effectId">The effect id from <see cref="EffectItem.DragFormat"/>.</param>
    /// <param name="trackId">The track under the drop.</param>
    /// <param name="frame">The frame under the drop.</param>
    /// <returns>The result, or null when nothing applied.</returns>
    public EditResult? ApplyAt(string effectId, string trackId, long frame)
    {
        ArgumentNullException.ThrowIfNull(trackId);
        var project = _timeline.Session.Current;
        if (Find(effectId) is not { } item || TimelineQuery.FindTrack(project, trackId) is not { } track)
        {
            return null;
        }

        if (track.Locked)
        {
            StatusText = track.Name + " is locked.";
            return null;
        }

        switch (item.Category)
        {
            case EffectCategory.Transition when item.TransitionKind is { } kind:
                if ((kind == TransitionKind.AudioCrossfade) != (track.Kind == TrackKind.Audio))
                {
                    StatusText = kind == TransitionKind.AudioCrossfade
                        ? "Drop Audio Crossfade on an audio cut."
                        : "Drop video transitions on a video cut.";
                    return null;
                }

                var cuts = Cuts(track).ToList();
                if (cuts.Count == 0)
                {
                    StatusText = "There is no cut on " + track.Name + ".";
                    return null;
                }

                return AddTransitions(kind, [(track.Id, cuts.MinBy(c => Math.Abs(c - frame)))]);
            case EffectCategory.Title when item.Title is { } spec:
                return track.Kind == TrackKind.Video ? AddTitle(spec, frame, track.Id) : null;
            case EffectCategory.Adjustment when TimelineQuery.ClipAt(track, frame) is { } clip:
                // Like a click, a drop on a clip also reaches its linked partners while linked selection is on.
                return Fade(item.Id, _timeline.LinkedSelection ? TimelineQuery.WithLinkedPartners(project, [clip.Id]) : [clip.Id]);
            default:
                return null;
        }
    }

    /// <summary>
    /// What <see cref="ApplyAt"/> would change, for the drop ghost: the cut nearest <paramref name="frame"/> with a 1 s
    /// span centred on it, a 5 s title span at <paramref name="frame"/>, or the clip under <paramref name="frame"/>.
    /// Null when the drop would do nothing (wrong track kind, a locked track, no cut or no clip).
    /// </summary>
    /// <param name="effectId">The effect id from <see cref="EffectItem.DragFormat"/>.</param>
    /// <param name="trackId">The hovered track.</param>
    /// <param name="frame">The frame under the pointer.</param>
    /// <returns>The preview, or null.</returns>
    public EffectDropPreview? PreviewAt(string effectId, string trackId, long frame)
    {
        var project = _timeline.Session.Current;
        if (Find(effectId) is not { } item || TimelineQuery.FindTrack(project, trackId) is not { Locked: false } track)
        {
            return null;
        }

        var rate = project.Sequence.Settings.Rate;
        switch (item.Category)
        {
            case EffectCategory.Transition when item.TransitionKind is { } kind
                && (kind == TransitionKind.AudioCrossfade) == (track.Kind == TrackKind.Audio):
                var cuts = Cuts(track).ToList();
                if (cuts.Count == 0)
                {
                    return null;
                }

                var cut = cuts.MinBy(c => Math.Abs(c - frame));
                var length = FramesFor(TransitionLengthSeconds, rate);
                return new EffectDropPreview(track.Id, cut - (length / 2), length, EffectCategory.Transition);
            case EffectCategory.Title when item.Title is not null && track.Kind == TrackKind.Video:
                return new EffectDropPreview(track.Id, Math.Max(0, frame), FramesFor(TitleLengthSeconds, rate), EffectCategory.Title);
            case EffectCategory.Adjustment when TimelineQuery.ClipAt(track, frame) is { } clip:
                return new EffectDropPreview(track.Id, clip.Start, clip.Duration, EffectCategory.Adjustment);
            default:
                return null;
        }
    }

    partial void OnDefaultTransitionKindChanged(TransitionKind value)
    {
        MarkDefault(value);
        DefaultTransitionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void MarkDefault(TransitionKind kind)
    {
        foreach (var item in Transitions)
        {
            item.IsDefault = item.TransitionKind == kind;
        }
    }

    partial void OnSelectedItemChanged(EffectItem? oldValue, EffectItem? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    private static IEnumerable<long> Cuts(Track track)
    {
        for (var i = 0; i + 1 < track.Clips.Length; i++)
        {
            if (track.Clips[i].End == track.Clips[i + 1].Start)
            {
                yield return track.Clips[i].End;
            }
        }
    }

    private static EffectItem TitleItem(string key, string name, string description, TitleSpec spec) =>
        new(EffectItem.TitlePrefix + key, name, EffectCategory.Title, "T", "5 s", description + " Places a 5 s title at the playhead (Ctrl+T adds Lower Third).")
        {
            Title = spec,
        };

    /// <summary>Selects an item (a click in the list).</summary>
    /// <param name="item">The item.</param>
    [RelayCommand]
    private void Select(EffectItem? item) => SelectedItem = item;

    /// <summary>Applies an item (its Apply button).</summary>
    /// <param name="item">The item.</param>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void ApplyItem(EffectItem? item)
    {
        if (item is not null)
        {
            SelectedItem = item;
            Apply(item);
        }
    }

    /// <summary>Applies the selected item (the panel's Apply button).</summary>
    [RelayCommand(CanExecute = nameof(CanApplySelected))]
    private void ApplySelected()
    {
        if (SelectedItem is { } item)
        {
            Apply(item);
        }
    }

    private bool CanApply() => !IsReadOnly;

    private bool CanApplySelected() => SelectedItem is not null && !IsReadOnly;

    /// <summary>Double-click: a video transition becomes the default (DESIGN 4.2); anything else applies.</summary>
    /// <param name="item">The item.</param>
    [RelayCommand]
    private void Activate(EffectItem? item)
    {
        if (item is null)
        {
            return;
        }

        SelectedItem = item;
        if (item.TransitionKind is { } kind && kind != TransitionKind.AudioCrossfade)
        {
            DefaultTransitionKind = kind;
            StatusText = EffectItem.TransitionName(kind) + " is the default transition (Ctrl+D).";
        }
        else if (IsReadOnly)
        {
            StatusText = ReadOnlyStatus;
        }
        else
        {
            Apply(item);
        }
    }

    /// <summary>Makes a video transition the default.</summary>
    /// <param name="item">The item.</param>
    [RelayCommand]
    private void SetDefault(EffectItem? item)
    {
        if (item?.TransitionKind is { } kind && kind != TransitionKind.AudioCrossfade)
        {
            DefaultTransitionKind = kind;
        }
    }

    /// <summary>Ctrl+D: the default video transition on the selected clips' cuts or the cut nearest the playhead.</summary>
    [RelayCommand]
    private void AddDefaultTransition() => ApplyTransition(DefaultTransitionKind);

    /// <summary>Ctrl+Shift+D: an audio crossfade on the selected audio cuts or the audio cut nearest the playhead.</summary>
    [RelayCommand]
    private void AddAudioCrossfade() => ApplyTransition(TransitionKind.AudioCrossfade);

    /// <summary>Ctrl+T: a Lower Third title at the playhead.</summary>
    [RelayCommand]
    private void NewTitle()
    {
        if (Titles.FirstOrDefault(t => t.Id == EffectItem.TitlePrefix + "LowerThird")?.Title is { } spec)
        {
            AddTitle(spec, _timeline.Playhead, null);
        }
    }

    private EditResult? ApplyTransition(TransitionKind kind)
    {
        var project = _timeline.Session.Current;
        var audio = kind == TransitionKind.AudioCrossfade;
        if (_timeline.SelectedTransitionId is { } selected
            && TimelineQuery.FindTransition(project, selected) is { } found
            && (found.Track.Kind == TrackKind.Audio) == audio)
        {
            var changed = _timeline.Run(p => TimelineEdits.SetTransitionKind(p, [selected], kind));
            StatusText = changed.Changed(project) ? "Changed the transition to " + EffectItem.TransitionName(kind) + "." : string.Empty;
            return changed;
        }

        var cuts = CutsForSelection(project, audio);
        if (cuts.IsEmpty)
        {
            StatusText = audio ? "No audio cut to add a crossfade to." : "No video cut to add a transition to.";
            return null;
        }

        return AddTransitions(kind, cuts);
    }

    private EditResult AddTransitions(TransitionKind kind, IReadOnlyList<(string TrackId, long CutFrame)> cuts)
    {
        var project = _timeline.Session.Current;
        var duration = FramesFor(TransitionLengthSeconds, project.Sequence.Settings.Rate);
        var ids = _timeline.Ids;
        var result = _timeline.Run(p => TimelineEdits.AddTransitions(ids, p, cuts, kind, duration));
        var count = result.Select.IsDefault ? 0 : result.Select.Length;
        StatusText = count switch
        {
            0 => result.Warnings.IsDefaultOrEmpty ? "No transition added." : result.Warnings[0],
            1 => "Added " + EffectItem.TransitionName(kind) + ".",
            _ => string.Create(CultureInfo.InvariantCulture, $"Added {count} transitions."),
        };
        if (count == 1)
        {
            _timeline.SelectTransition(result.Select[0]);
        }

        return result;
    }

    private EditResult? ApplyAdjustment(string effectId)
    {
        var ids = TimelineQuery.ExistingIds(_timeline.Session.Current, _timeline.SelectedClipIds);
        if (ids.IsEmpty)
        {
            StatusText = "Select clips to fade.";
            return null;
        }

        return Fade(effectId, ids);
    }

    private EditResult? Fade(string effectId, IReadOnlyCollection<string> clipIds)
    {
        var project = _timeline.Session.Current;
        var frames = FramesFor(FadeSeconds, project.Sequence.Settings.Rate);
        var name = effectId[EffectItem.AdjustmentPrefix.Length..];
        (long? In, long? Out, string Label) spec = name switch
        {
            "FadeIn" => (frames, null, "Fade In"),
            "FadeOut" => (null, frames, "Fade Out"),
            "FadeInOut" => (frames, frames, "Fade In and Out"),
            "RemoveFades" => (0, 0, "Remove Fades"),
            _ => (null, null, string.Empty),
        };
        if (spec.In is null && spec.Out is null)
        {
            return null;
        }

        var result = _timeline.Run(p => TimelineEdits.UpdateClips(p, clipIds, c => WithFades(c, spec.In, spec.Out), spec.Label));
        StatusText = result.Changed(project) ? spec.Label + " applied." : "Nothing to change.";
        return result;
    }

    private static Clip WithFades(Clip clip, long? fadeIn, long? fadeOut) => clip switch
    {
        // Each fade is limited to half the clip so a 1 s in and out pair also fits short clips (sanitize then clamps).
        MediaClip m => m with
        {
            Video = m.Video with { FadeInFrames = Limit(fadeIn, m.Duration) ?? m.Video.FadeInFrames, FadeOutFrames = Limit(fadeOut, m.Duration) ?? m.Video.FadeOutFrames },
            Audio = m.Audio with { FadeInFrames = Limit(fadeIn, m.Duration) ?? m.Audio.FadeInFrames, FadeOutFrames = Limit(fadeOut, m.Duration) ?? m.Audio.FadeOutFrames },
        },
        TitleClip t => t with { FadeInFrames = Limit(fadeIn, t.Duration) ?? t.FadeInFrames, FadeOutFrames = Limit(fadeOut, t.Duration) ?? t.FadeOutFrames },
        _ => clip,
    };

    private static long? Limit(long? frames, long duration) => frames is { } f ? Math.Min(f, duration / 2) : null;

    private EditResult? AddTitle(TitleSpec spec, long frame, string? trackId)
    {
        var project = _timeline.Session.Current;
        var duration = FramesFor(TitleLengthSeconds, project.Sequence.Settings.Rate);
        var at = Math.Max(0, frame);
        var ids = _timeline.Ids;
        // Gap filled: the template sizes are for a 1080-line canvas, so they scale with the sequence height.
        var scale = project.Sequence.Settings.Height / 1080.0;
        var scaled = spec with
        {
            Size = Math.Max(1, Math.Round(spec.Size * scale)),
            OutlineWidth = Math.Round(spec.OutlineWidth * scale, 1),
            BoxPadding = Math.Round(spec.BoxPadding * scale),
        };
        var title = new TitleClip("c_title", 0, duration, true, scaled, 1, 0, 0);
        var result = _timeline.Run(p =>
        {
            var target = trackId;
            var start = p;
            if (target is null)
            {
                target = TitleTrack(p, at, duration);
                if (target is null)
                {
                    var added = TimelineEdits.AddTrack(ids, p, TrackKind.Video);
                    if (added.Changed(p))
                    {
                        start = added.Project;
                        target = added.Select[0];
                    }
                    else
                    {
                        target = TimelineQuery.TracksOfKind(p, TrackKind.Video).LastOrDefault(t => !t.Locked)?.Id;
                    }
                }
            }

            if (target is null)
            {
                return EditResult.Unchanged(p, "Add Title", "Every video track is locked.");
            }

            var placed = TimelineEdits.Place(ids, start, [new ClipPlacement(target, title)], at, EditMode.Overwrite);
            if (!placed.Changed(start))
            {
                return EditResult.Unchanged(p, "Add Title", [.. placed.Warnings]);
            }

            return placed with { Label = "Add Title" };
        });
        if (!result.Select.IsDefaultOrEmpty)
        {
            _timeline.Select(result.Select, false);
            StatusText = "Added a title at " + Timecode.Format(at, project.Sequence.Settings.Rate) + ".";
        }
        else
        {
            StatusText = result.Warnings.IsDefaultOrEmpty ? "No title added." : result.Warnings[0];
        }

        return result;
    }

    /// <summary>
    /// The track a new title goes to: the lowest unlocked video track that sits above every video track with a clip
    /// under the playhead and is free over the title's range. Null when there is none.
    /// </summary>
    private static string? TitleTrack(Project project, long at, long duration)
    {
        var videos = TimelineQuery.TracksOfKind(project, TrackKind.Video);
        var highestBusy = -1;
        for (var i = 0; i < videos.Length; i++)
        {
            if (TimelineQuery.ClipAt(videos[i], at) is not null)
            {
                highestBusy = i;
            }
        }

        for (var i = highestBusy + 1; i < videos.Length; i++)
        {
            var track = videos[i];
            if (!track.Locked && track.Clips.All(c => c.End <= at || c.Start >= at + duration))
            {
                return track.Id;
            }
        }

        return null;
    }
}
