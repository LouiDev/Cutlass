namespace Cutlass.App.ViewModels;

/// <summary>
/// One item of the Agent menu's "Copy briefing for" submenu (AGENT_INTEGRATION 9.4): a client profile (or the plain
/// HTTP variant) and the command id that copies its briefing through <c>RunCommandCommand</c>.
/// </summary>
/// <param name="TargetId">The profile id, or <c>http</c>.</param>
/// <param name="Header">The menu text (underscores doubled, so they do not become access keys).</param>
/// <param name="CommandId">The command id (<see cref="Services.CommandIds.AgentCopyBriefingFor"/>).</param>
/// <param name="Command">The main window's <c>RunCommandCommand</c>, run with <paramref name="CommandId"/>.</param>
public sealed record BriefingTargetItem(string TargetId, string Header, string CommandId, System.Windows.Input.ICommand Command);
