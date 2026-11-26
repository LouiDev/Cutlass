using Cutlass.Compiler.Export;

namespace Cutlass.App.ViewModels.Dialogs;

/// <summary>One entry of the export dialog's preset list, greyed out with a reason when the build lacks it.</summary>
/// <param name="Preset">The preset.</param>
/// <param name="UnavailableReason">Why it is unavailable, for example "libx265 is not in this ffmpeg build", or null.</param>
public sealed record ExportPresetOption(ExportPreset Preset, string? UnavailableReason)
{
    /// <summary>True when this ffmpeg build can run the preset.</summary>
    public bool IsAvailable => UnavailableReason is null;

    /// <summary>The name shown in the list.</summary>
    public string DisplayName => Preset.DisplayName + " (" + Preset.Extension + ")";

    /// <summary>The tooltip: the reason when unavailable, else the container.</summary>
    public string ToolTip => UnavailableReason ?? Preset.DisplayName + ", " + Preset.Extension;
}
