using Avalonia.Controls;

namespace Cutlass.App.Views.Agent;

/// <summary>The agent lock banner (AGENT_INTEGRATION 6.2); its DataContext is the <see cref="ViewModels.AgentViewModel"/>.</summary>
public sealed partial class AgentBanner : UserControl
{
    /// <summary>Creates the banner and loads its XAML.</summary>
    public AgentBanner()
    {
        InitializeComponent();
    }
}
