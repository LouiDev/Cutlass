using Avalonia.Controls;

namespace Cutlass.App.Views;

/// <summary>The agent client rows (AGENT_INTEGRATION 9.4) shared by the Connect dialog and Settings > Agents.</summary>
public sealed partial class ClientListView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ClientListView()
    {
        InitializeComponent();
    }
}
