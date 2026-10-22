using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The inspector for a selected transition (DESIGN 4.4): kind and duration in frames (with the seconds), the clips it
/// joins, the most frames the cut allows and a red notice when the last duration change was clamped. Transitions are
/// always centred on their cut (TECH_BRIEF 3.2 has no alignment), so there is no alignment field.
/// </summary>
public sealed partial class TransitionSectionViewModel : InspectorSection
{
    private string? _lastClamp;

    /// <summary>Creates the section.</summary>
    /// <param name="editor">The session path.</param>
    /// <param name="states">The remembered expanded states.</param>
    /// <param name="transitionId">The selected transition.</param>
    public TransitionSectionViewModel(InspectorEditor editor, IDictionary<string, bool> states, string transitionId)
        : base(editor, states, "transition:" + transitionId, "TRANSITION")
    {
        TransitionId = transitionId;
        var group = AddGroup("TRANSITION");
        KindRow = group.Add(new InspectorChoiceRow(editor, "Kind", InspectorEditor.CoalesceKey([transitionId], "transition.kind"), v => v is TransitionKind kind
            ? p => TimelineEdits.SetTransitionKind(p, [transitionId], kind)
            : null));
        DurationRow = group.Add(new InspectorNumberField(
            editor,
            "Duration",
            InspectorEditor.CoalesceKey([transitionId], "transition.duration"),
            "Transition Duration",
            v => p =>
            {
                var result = TimelineEdits.SetTransitionDuration(p, transitionId, (long)v);
                _lastClamp = result.Warnings.IsDefaultOrEmpty ? null : result.Warnings[0];
                return result;
            })
        {
            Minimum = TimelineEdits.MinTransitionFrames,
            Maximum = 100_000,
            Step = 0.25,
            Decimals = 0,
            Unit = "f",
        });
        SecondsRow = group.Add(new InspectorInfoRow("Seconds"));
        MaximumRow = group.Add(new InspectorInfoRow("Maximum"));
        ClipsRow = group.Add(new InspectorInfoRow("Between"));
        ClampNotice = group.Add(new InspectorNoticeRow(isDanger: true));
        group.Add(new InspectorButtonRow(string.Empty, "Remove transition", new RelayCommand(() =>
            Run(p => TimelineEdits.RemoveTransitions(p, [transitionId])))));
    }

    /// <summary>The selected transition id.</summary>
    public string TransitionId { get; }

    /// <summary>The kind.</summary>
    public InspectorChoiceRow KindRow { get; }

    /// <summary>The duration in frames.</summary>
    public InspectorNumberField DurationRow { get; }

    /// <summary>The duration in seconds.</summary>
    public InspectorInfoRow SecondsRow { get; }

    /// <summary>The most frames the cut allows.</summary>
    public InspectorInfoRow MaximumRow { get; }

    /// <summary>The joined clips.</summary>
    public InspectorInfoRow ClipsRow { get; }

    /// <summary>The red clamp notice, such as "Shortened to 12 frames: not enough source after intro.mov".</summary>
    public InspectorNoticeRow ClampNotice { get; }

    /// <summary>The kinds a track of <paramref name="kind"/> accepts (TECH_BRIEF 3.2).</summary>
    /// <param name="kind">The track kind.</param>
    /// <returns>The choices.</returns>
    public static IReadOnlyList<InspectorChoice> KindChoices(TrackKind kind) => kind == TrackKind.Audio
        ? [new InspectorChoice(TransitionKind.AudioCrossfade, EffectItem.TransitionName(TransitionKind.AudioCrossfade))]
        : [.. Enum.GetValues<TransitionKind>().Where(k => k != TransitionKind.AudioCrossfade).Select(k => new InspectorChoice(k, EffectItem.TransitionName(k)))];

    /// <inheritdoc />
    public override void Refresh(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (TimelineQuery.FindTransition(project, TransitionId) is not { } found)
        {
            return;
        }

        var (track, transition) = found;
        var rate = project.Sequence.Settings.Rate;
        KindRow.SetOptions(KindChoices(track.Kind));
        KindRow.IsEnabled = track.Kind == TrackKind.Video;
        KindRow.Load(transition.Kind);
        DurationRow.Load(transition.Duration);
        SecondsRow.Text = Seconds(transition.Duration, rate);
        var left = track.Clips.FirstOrDefault(c => c.Id == transition.LeftClipId);
        var right = track.Clips.FirstOrDefault(c => c.Id == transition.RightClipId);
        if (left is not null && right is not null)
        {
            var max = TimelineQuery.MaxTransitionDuration(project, track, left, right);
            MaximumRow.Text = string.Create(CultureInfo.InvariantCulture, $"{max} frames ({Seconds(max, rate)})");
            ClipsRow.Text = TimelineQuery.DisplayName(project, left) + "  |  " + TimelineQuery.DisplayName(project, right);
        }

        // DESIGN 4.4: the red notice shows whenever the duration was clamped, also by Ctrl+D, a drop or a cut edit, so
        // it matches the timeline's red corner flag; a clamp from this section's own field keeps its exact message.
        ClampNotice.Text = _lastClamp ?? LimitText(project, TransitionId) ?? string.Empty;
    }

    /// <summary>
    /// The clamp text of a transition that sits at its maximum duration (the DESIGN 5.5 red corner flag and the DESIGN 4.4
    /// red notice), or null when it has room to grow.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="transitionId">The transition.</param>
    /// <returns>The text, or null.</returns>
    public static string? LimitText(Project project, string transitionId)
    {
        if (TimelineQuery.FindTransition(project, transitionId) is not { } found)
        {
            return null;
        }

        var (track, transition) = found;
        var left = track.Clips.FirstOrDefault(c => c.Id == transition.LeftClipId);
        var right = track.Clips.FirstOrDefault(c => c.Id == transition.RightClipId);
        if (left is null || right is null)
        {
            return null;
        }

        var max = TimelineQuery.MaxTransitionDuration(project, track, left, right);
        if (transition.Duration < max)
        {
            return null;
        }

        // The binding limit, in the order of the Core budget: source handles first, then clip lengths.
        var (a, b) = TimelineQuery.TransitionHalves(transition.Duration);
        string reason;
        if (TimelineQuery.TailHandle(project, left) <= b)
        {
            reason = "not enough source after " + TimelineQuery.DisplayName(project, left);
        }
        else if (TimelineQuery.HeadHandle(project, right) <= a)
        {
            reason = "not enough source before " + TimelineQuery.DisplayName(project, right);
        }
        else
        {
            reason = "the clips are too short";
        }

        return "Shortened to " + transition.Duration.ToString(CultureInfo.InvariantCulture) + " frames: " + reason;
    }

    private static string Seconds(long frames, FrameRate rate) =>
        (frames * rate.Value.Den / (double)rate.Value.Num).ToString("0.00", CultureInfo.InvariantCulture) + " s";
}
