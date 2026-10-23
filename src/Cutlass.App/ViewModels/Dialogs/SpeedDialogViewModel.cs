using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels.Dialogs;

/// <summary>
/// The speed dialog (Ctrl+R, DESIGN 5.5): 25 to 400 percent with presets, "Ripple following clips" (on by default)
/// and a preview of the new duration. The source range is kept, so the duration changes. The model has no reverse
/// playback (TECH_BRIEF 3.2), so the dialog offers none. Apply raises <see cref="CloseRequested"/> with true; the
/// caller then runs <see cref="CreateEdit"/>.
/// </summary>
public sealed partial class SpeedDialogViewModel : ObservableObject
{
    private readonly Project _project;
    private readonly MediaClip? _primary;

    /// <summary>Creates the dialog model.</summary>
    /// <param name="project">The project.</param>
    /// <param name="clipIds">The selected clips (titles and stills are skipped by the edit).</param>
    public SpeedDialogViewModel(Project project, IReadOnlyCollection<string> clipIds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(clipIds);
        _project = project;
        ClipIds = [.. clipIds];
        var clips = ClipIds.Select(id => TimelineQuery.FindClip(project, id)?.Clip).OfType<MediaClip>()
            .Where(m => TimelineQuery.FindMedia(project, m.MediaId) is { Info.IsStill: false })
            .OrderBy(m => m.Start)
            .ToList();
        _primary = clips.FirstOrDefault();
        ClipCount = clips.Count;
        Percent = _primary?.SpeedPercent ?? 100;
        RippleFollowing = true;
    }

    /// <summary>Raised by Apply (true) and Cancel (false).</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>The selected clip ids.</summary>
    public ImmutableArray<string> ClipIds { get; }

    /// <summary>The number of clips whose speed can change.</summary>
    public int ClipCount { get; }

    /// <summary>The preset percentages.</summary>
    public IReadOnlyList<int> Presets { get; } = [25, 50, 100, 200, 400];

    /// <summary>The lowest speed.</summary>
    public double Minimum => TimelineEdits.MinSpeedPercent;

    /// <summary>The highest speed.</summary>
    public double Maximum => TimelineEdits.MaxSpeedPercent;

    /// <summary>The new speed in percent (whole numbers, 25 to 400).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    public partial double Percent { get; set; }

    /// <summary>True to ripple following clips (the default).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewText))]
    public partial bool RippleFollowing { get; set; }

    /// <summary>True when there is a clip whose speed can change.</summary>
    public bool CanApply => ClipCount > 0;

    /// <summary>The applied percentage: <see cref="Percent"/> rounded and clamped.</summary>
    public int PercentValue => (int)Math.Clamp(Math.Round(double.IsNaN(Percent) ? 100 : Percent), TimelineEdits.MinSpeedPercent, TimelineEdits.MaxSpeedPercent);

    /// <summary>The preview, for example "Duration 00:00:04:00 becomes 00:00:02:00".</summary>
    public string PreviewText
    {
        get
        {
            if (_primary is null)
            {
                return "Speed applies to video and audio clips only.";
            }

            var rate = _project.Sequence.Settings.Rate;
            var text = "Duration " + Timecode.Format(_primary.Duration, rate) + " becomes " + Timecode.Format(NewDuration(), rate);
            return ClipCount > 1
                ? text + string.Create(CultureInfo.InvariantCulture, $" (first of {ClipCount} clips)")
                : text;
        }
    }

    /// <summary>The duration the first clip gets: <c>round(Duration * old / new)</c>, limited by the source.</summary>
    /// <returns>The new duration in frames (0 without a clip).</returns>
    public long NewDuration()
    {
        if (_primary is null)
        {
            return 0;
        }

        var percent = PercentValue;
        var scaled = (Int128)_primary.Duration * _primary.SpeedPercent;
        var duration = Math.Max(1, (long)(((2 * scaled) + percent) / (2 * percent)));
        if (TimelineQuery.FindMedia(_project, _primary.MediaId) is { } media)
        {
            duration = Math.Min(duration, Math.Max(1, TimelineQuery.MaxFramesFrom(_primary, media, _project.Sequence.Settings.Rate, percent)));
        }

        return duration;
    }

    /// <summary>The speed edit for the dialog's values.</summary>
    /// <returns>The edit.</returns>
    public Func<Project, EditResult> CreateEdit()
    {
        var ids = ClipIds;
        var percent = PercentValue;
        var ripple = RippleFollowing;
        return p => TimelineEdits.SetSpeed(p, ids, percent, ripple);
    }

    partial void OnPercentChanged(double value)
    {
        var clamped = PercentValue;
        if (!value.Equals(clamped))
        {
            Percent = clamped;
        }
    }

    /// <summary>Sets a preset percentage.</summary>
    /// <param name="percent">The preset.</param>
    [RelayCommand]
    private void SetPreset(int percent) => Percent = percent;

    /// <summary>Closes the dialog and applies.</summary>
    [RelayCommand]
    private void Apply()
    {
        if (CanApply)
        {
            CloseRequested?.Invoke(this, true);
        }
    }

    /// <summary>Closes the dialog without changes.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
