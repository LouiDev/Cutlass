using Avalonia.Controls;

namespace Cutlass.App.Views;

/// <summary>The AGENT tab of the left panel (AGENT_INTEGRATION 6.3); its DataContext is the <see cref="ViewModels.AgentViewModel"/>.</summary>
public sealed partial class AgentPanelView : UserControl
{
    /// <summary>Creates the panel and loads its XAML.</summary>
    public AgentPanelView()
    {
        InitializeComponent();
    }
}
