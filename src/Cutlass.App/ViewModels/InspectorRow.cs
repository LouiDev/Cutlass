using CommunityToolkit.Mvvm.ComponentModel;

namespace Cutlass.App.ViewModels;

/// <summary>
/// One dense inspector row: a label on the left and a field on the right (DESIGN 4.4, 8). Each concrete row type has a
/// DataTemplate in <c>Views/InspectorView.axaml</c>. Rows load model values inside <see cref="Loading"/>, so loading
/// never writes back to the session.
/// </summary>
public abstract partial class InspectorRow : ObservableObject
{
    /// <summary>Creates the row.</summary>
    /// <param name="label">The label shown on the left.</param>
    protected InspectorRow(string label)
    {
        Label = label;
        IsVisible = true;
    }

    /// <summary>The label shown on the left.</summary>
    public string Label { get; }

    /// <summary>False hides the row (a field that does not apply to the current selection).</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>True while the row loads model values (setters then skip the session).</summary>
    protected bool Loading { get; private set; }

    /// <summary>Runs <paramref name="load"/> with <see cref="Loading"/> set.</summary>
    /// <param name="load">Assigns the model values to the row's properties.</param>
    protected void RunLoading(Action load)
    {
        ArgumentNullException.ThrowIfNull(load);
        var outer = Loading;
        Loading = true;
        try
        {
            load();
        }
        finally
        {
            Loading = outer;
        }
    }
}
