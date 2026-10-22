namespace Cutlass.App.ViewModels;

/// <summary>
/// One entry of the first-start dialog's "Register with" dropdown (AGENT_INTEGRATION 9.4): a detected client row, or
/// "none".
/// </summary>
/// <param name="Item">The client row, or null for "none".</param>
public sealed record McpClientChoice(AgentClientItemViewModel? Item)
{
    /// <summary>The "none" entry.</summary>
    public static McpClientChoice None { get; } = new((AgentClientItemViewModel?)null);

    /// <summary>The entry as the dropdown shows it: the client's name and state, or "none".</summary>
    public string DisplayText => Item is { } item ? item.Name + " (" + item.StateText + ")" : "none";
}
