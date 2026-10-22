using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cutlass.App.ViewModels;

/// <summary>A row with a button: the speed button ("100%"), "Close gap", "Change...", "Add keyframe".</summary>
public sealed partial class InspectorButtonRow : InspectorRow
{
    /// <summary>Creates the row.</summary>
    /// <param name="label">The row label (empty for a full-width action).</param>
    /// <param name="buttonText">The button text.</param>
    /// <param name="command">The command.</param>
    public InspectorButtonRow(string label, string buttonText, ICommand command)
        : base(label)
    {
        ArgumentNullException.ThrowIfNull(command);
        ButtonText = buttonText;
        Command = command;
    }

    /// <summary>The button text.</summary>
    [ObservableProperty]
    public partial string ButtonText { get; set; }

    /// <summary>The command.</summary>
    public ICommand Command { get; }

    /// <summary>The button tooltip, for example the shortcut.</summary>
    public string? ToolTip { get; init; }

    /// <summary>True when the row has a label (the button then sits in the field column).</summary>
    public bool HasLabel => Label.Length > 0;
}
