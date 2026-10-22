namespace Cutlass.App.ViewModels;

/// <summary>
/// The Agent menu actions the Agent panel's header block and the status bar badge reuse (AGENT_INTEGRATION 6.6 and
/// 9.4), so a panel button and its menu item run the same path. <see cref="MainWindowViewModel"/> implements it.
/// </summary>
public interface IAgentMenuActions
{
    /// <summary>Agent > Start/Stop MCP server (with the first-start dialog, or the take-over question on Stop).</summary>
    /// <returns>A task that completes when the server switched or the user cancelled.</returns>
    Task ToggleServerAsync();

    /// <summary>
    /// Agent > Copy briefing for your agent: the neutral body with the tail of a target client. Null is the client
    /// registered last (or the generic tail when none was); <c>http</c> is the plain HTTP variant.
    /// </summary>
    /// <param name="targetId">The profile id, <c>http</c>, or null.</param>
    /// <returns>A task that completes when the text is on the clipboard.</returns>
    Task CopyBriefingAsync(string? targetId);

    /// <summary>Agent > Connect an agent client... (the client list).</summary>
    /// <returns>A task that completes when the dialog closed.</returns>
    Task ConnectClientsAsync();

    /// <summary>Settings, Agents page (the badge while the server failed).</summary>
    /// <returns>A task that completes when the Settings dialog closed.</returns>
    Task ShowAgentSettingsAsync();
}
