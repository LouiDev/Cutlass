using CommunityToolkit.Mvvm.ComponentModel;

namespace Cutlass.App.ViewModels;

/// <summary>A wrapped notice under a group: a hint, or a red warning such as the transition clamp notice.</summary>
public sealed partial class InspectorNoticeRow : InspectorRow
{
    /// <summary>Creates the row.</summary>
    /// <param name="isDanger">True for a red (Danger) notice.</param>
    public InspectorNoticeRow(bool isDanger)
        : base(string.Empty)
    {
        IsDanger = isDanger;
        Text = string.Empty;
        IsVisible = false;
    }

    /// <summary>True for a red notice.</summary>
    public bool IsDanger { get; }

    /// <summary>The notice text; the row hides while it is empty.</summary>
    [ObservableProperty]
    public partial string Text { get; set; }

    partial void OnTextChanged(string value) => IsVisible = !string.IsNullOrEmpty(value);
}
