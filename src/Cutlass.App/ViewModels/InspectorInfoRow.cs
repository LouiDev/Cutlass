using CommunityToolkit.Mvvm.ComponentModel;

namespace Cutlass.App.ViewModels;

/// <summary>A read-only inspector row: a label and a value text (file path, frame size, gap length).</summary>
public sealed partial class InspectorInfoRow : InspectorRow
{
    /// <summary>Creates the row.</summary>
    /// <param name="label">The row label.</param>
    public InspectorInfoRow(string label)
        : base(label)
    {
        Text = string.Empty;
    }

    /// <summary>The value text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip))]
    public partial string Text { get; set; }

    /// <summary>The tooltip (the full text, useful for long paths).</summary>
    public string? ToolTip => string.IsNullOrEmpty(Text) ? null : Text;
}
