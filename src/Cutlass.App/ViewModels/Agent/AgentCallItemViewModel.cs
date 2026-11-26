using System.Globalization;
using Cutlass.Agent.Tools.Audit;

namespace Cutlass.App.ViewModels.Agent;

/// <summary>
/// One row of the Agent panel's call log (AGENT_INTEGRATION 6.3): time, agent, tool, duration and code (Danger for
/// errors), with the one-line argument summary as the tooltip. Immutable.
/// </summary>
public sealed class AgentCallItemViewModel
{
    /// <summary>Creates the row from an audit entry.</summary>
    /// <param name="entry">The entry.</param>
    public AgentCallItemViewModel(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Entry = entry;
        TimeText = entry.TimeUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        DurationText = entry.DurationMs.ToString(CultureInfo.InvariantCulture) + " ms";
        ToolTip = string.IsNullOrEmpty(entry.Arguments) ? entry.Tool : entry.Tool + " " + entry.Arguments;
    }

    /// <summary>The audit entry.</summary>
    public AuditEntry Entry { get; }

    /// <summary>The local time, for example <c>14:03:12</c>.</summary>
    public string TimeText { get; }

    /// <summary>The agent's name.</summary>
    public string Agent => Entry.Agent;

    /// <summary>The tool, for example <c>edit.split</c>.</summary>
    public string Tool => Entry.Tool;

    /// <summary>The duration, for example <c>12 ms</c>.</summary>
    public string DurationText { get; }

    /// <summary><c>ok</c> or the error code.</summary>
    public string Code => Entry.Code;

    /// <summary>True when the call failed (the code shows in Danger).</summary>
    public bool IsError => Entry.IsError;

    /// <summary>The tool and its argument summary.</summary>
    public string ToolTip { get; }
}
