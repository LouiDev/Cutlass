namespace Cutlass.App.ViewModels;

/// <summary>An output size choice of the export dialog (DESIGN 9).</summary>
/// <param name="Label">The text shown, for example "1080p (1920x1080)".</param>
/// <param name="Width">The output width (even), 0 for the custom entry.</param>
/// <param name="Height">The output height (even), 0 for the custom entry.</param>
public sealed record ExportResolutionOption(string Label, int Width, int Height)
{
    /// <summary>The custom entry: the dialog's height field decides the size, with the aspect locked.</summary>
    public static ExportResolutionOption Custom { get; } = new("Custom height...", 0, 0);

    /// <summary>True for the custom entry.</summary>
    public bool IsCustom => Width == 0 && Height == 0;
}
