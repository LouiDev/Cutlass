using CommunityToolkit.Mvvm.Input;

namespace Cutlass.App.ViewModels.Agent;

/// <summary>
/// One discovery URL of the agent server (AGENT_INTEGRATION 9.2), listed in Settings > Agents > Connect clients with a
/// Copy button: the tool definitions per format, the OpenAPI document and the instructions.
/// </summary>
public sealed partial class AgentDiscoveryUrlItem
{
    private readonly Func<string, Task> _copy;

    /// <summary>Creates the item.</summary>
    /// <param name="label">What the URL returns, for example "Tools (markdown)".</param>
    /// <param name="url">The URL.</param>
    /// <param name="copy">Puts a text on the clipboard.</param>
    public AgentDiscoveryUrlItem(string label, string url, Func<string, Task> copy)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(copy);
        Label = label;
        Url = url;
        _copy = copy;
    }

    /// <summary>What the URL returns.</summary>
    public string Label { get; }

    /// <summary>The URL.</summary>
    public string Url { get; }

    /// <summary>Copies the URL.</summary>
    /// <returns>A task that completes when the URL is on the clipboard.</returns>
    [RelayCommand]
    private Task CopyAsync() => _copy(Url);
}
