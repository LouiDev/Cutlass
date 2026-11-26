using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Cutlass.App.ViewModels.Inspector;

/// <summary>
/// A collapsible inspector section such as TRANSFORM or COLOR (DESIGN 4.4). Its expanded state is remembered by title
/// for the whole session, and an optional Reset restores the group's defaults as one undo step.
/// </summary>
public sealed partial class InspectorGroup : ObservableObject
{
    private readonly IDictionary<string, bool> _states;
    private readonly Action? _reset;

    /// <summary>Creates the group.</summary>
    /// <param name="title">The caps title, for example <c>COLOR</c>.</param>
    /// <param name="states">The remembered expanded states, keyed by title.</param>
    /// <param name="reset">Restores the defaults, or null for no Reset button.</param>
    public InspectorGroup(string title, IDictionary<string, bool> states, Action? reset)
    {
        ArgumentNullException.ThrowIfNull(states);
        Title = title;
        _states = states;
        _reset = reset;
        IsVisible = true;
        IsExpanded = !states.TryGetValue(title, out var expanded) || expanded;
    }

    /// <summary>The caps title.</summary>
    public string Title { get; }

    /// <summary>The rows.</summary>
    public ObservableCollection<InspectorRow> Rows { get; } = [];

    /// <summary>True when the group has a Reset button.</summary>
    public bool CanReset => _reset is not null;

    /// <summary>True while the rows show.</summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>False hides the whole group (no selected item has these fields).</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    /// <summary>Adds a row and returns it.</summary>
    /// <typeparam name="T">The row type.</typeparam>
    /// <param name="row">The row.</param>
    /// <returns>The row.</returns>
    public T Add<T>(T row)
        where T : InspectorRow
    {
        Rows.Add(row);
        return row;
    }

    partial void OnIsExpandedChanged(bool value) => _states[Title] = value;

    /// <summary>Restores the group's defaults.</summary>
    [RelayCommand]
    private void Reset() => _reset?.Invoke();
}
