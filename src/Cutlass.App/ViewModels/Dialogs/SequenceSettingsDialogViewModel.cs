using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.ViewModels.Inspector;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.App.ViewModels.Dialogs;

/// <summary>
/// The sequence settings dialog ("Change..." in the inspector, DESIGN 4.4): width and height with presets (1080p, 4K,
/// vertical 1080x1920, 720p), the frame rate from <see cref="FrameRate.Supported"/>, and a warning that a rate change
/// converts every position by time and a size change re-fits clips to the new canvas. Apply raises
/// <see cref="CloseRequested"/> with true; the caller then runs <see cref="CreateEdit"/>
/// (<see cref="TimelineEdits.SetSequenceSettings"/>).
/// </summary>
public sealed partial class SequenceSettingsDialogViewModel : ObservableObject
{
    private readonly SequenceSettings _original;
    private bool _syncing;

    /// <summary>Creates the dialog model.</summary>
    /// <param name="current">The current settings.</param>
    /// <param name="hasClips">True when the sequence holds or held clips (the warning then applies).</param>
    public SequenceSettingsDialogViewModel(SequenceSettings current, bool hasClips)
    {
        ArgumentNullException.ThrowIfNull(current);
        _original = current;
        HasClips = hasClips;
        Rates = RateChoices();
        Width = current.Width;
        Height = current.Height;
        SelectedRate = Rates.FirstOrDefault(r => Equals(r.Value, current.Rate)) ?? Rates[0];
    }

    /// <summary>Raised by Apply (true) and Cancel (false).</summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>True when the sequence holds or held clips.</summary>
    public bool HasClips { get; }

    /// <summary>The size presets.</summary>
    public IReadOnlyList<SequenceSizePreset> Presets { get; } = SequenceSizePreset.All;

    /// <summary>The frame rates.</summary>
    public IReadOnlyList<InspectorChoice> Rates { get; }

    /// <summary>The width in pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarningText), nameof(HasWarning))]
    public partial double Width { get; set; }

    /// <summary>The height in pixels.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarningText), nameof(HasWarning))]
    public partial double Height { get; set; }

    /// <summary>The size preset matching the size, or Custom.</summary>
    [ObservableProperty]
    public partial SequenceSizePreset? SelectedPreset { get; set; }

    /// <summary>The frame rate.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WarningText), nameof(HasWarning))]
    public partial InspectorChoice? SelectedRate { get; set; }

    /// <summary>The new settings.</summary>
    public SequenceSettings Settings => new(
        (int)Math.Round(double.IsNaN(Width) ? _original.Width : Width),
        (int)Math.Round(double.IsNaN(Height) ? _original.Height : Height),
        SelectedRate?.Value is FrameRate rate ? rate : _original.Rate);

    /// <summary>True when the warning shows.</summary>
    public bool HasWarning => WarningText.Length > 0;

    /// <summary>The warning about re-fitting and rate conversion, empty when nothing relevant changes.</summary>
    public string WarningText
    {
        get
        {
            if (!HasClips)
            {
                return string.Empty;
            }

            var settings = Settings;
            var parts = new List<string>();
            if (settings.Rate != _original.Rate)
            {
                parts.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Changing the rate from {_original.Rate.DisplayName} to {settings.Rate.DisplayName} fps converts every clip, transition, fade, keyframe and marker by time; cuts can move by a frame and clips that no longer fit their source are trimmed."));
            }

            if (settings.Width != _original.Width || settings.Height != _original.Height)
            {
                parts.Add("Changing the frame size re-fits every clip to the new canvas; positions stay in pixels from the centre.");
            }

            return string.Join(" ", parts);
        }
    }

    /// <summary>The supported rates as combo box choices.</summary>
    /// <returns>The choices, slowest first.</returns>
    public static IReadOnlyList<InspectorChoice> RateChoices() =>
        [.. FrameRate.Supported.Select(r => new InspectorChoice(r, r.DisplayName + " fps"))];

    /// <summary>The settings edit for the dialog's values.</summary>
    /// <returns>The edit.</returns>
    public Func<Project, EditResult> CreateEdit()
    {
        var settings = Settings;
        return p => TimelineEdits.SetSequenceSettings(p, settings);
    }

    partial void OnWidthChanged(double value) => SyncPreset();

    partial void OnHeightChanged(double value) => SyncPreset();

    partial void OnSelectedPresetChanged(SequenceSizePreset? value)
    {
        if (_syncing || value is not { Width: > 0 } preset)
        {
            return;
        }

        _syncing = true;
        try
        {
            Width = preset.Width;
            Height = preset.Height;
        }
        finally
        {
            _syncing = false;
        }
    }

    private void SyncPreset()
    {
        if (_syncing)
        {
            return;
        }

        _syncing = true;
        try
        {
            var s = Settings;
            SelectedPreset = SequenceSizePreset.Find(s.Width, s.Height);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Closes the dialog and applies.</summary>
    [RelayCommand]
    private void Apply() => CloseRequested?.Invoke(this, true);

    /// <summary>Closes the dialog without changes.</summary>
    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
