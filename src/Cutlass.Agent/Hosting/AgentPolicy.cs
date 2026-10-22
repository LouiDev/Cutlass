using System.Collections.Immutable;
using Cutlass.Media.Settings;

namespace Cutlass.Agent.Hosting;

/// <summary>
/// An immutable snapshot of the agent settings and command line folders (AGENT_INTEGRATION 3.3, 4.6 and 6.1). Hosts
/// replace it when settings change.
/// </summary>
/// <param name="DefaultLeaseSeconds">The lease an agent gets unless it asks for another.</param>
/// <param name="MaxLeaseSeconds">The longest lease an agent may request.</param>
/// <param name="AskFirst">Ask the user before an agent takes the project.</param>
/// <param name="ReadFolders">Extra folders agents may read (Settings plus <c>--allow-read</c>).</param>
/// <param name="WriteFolders">Extra folders agents may write (Settings plus <c>--allow-write</c>).</param>
/// <param name="ExportFolder">The export folder setting, or null (then the project folder is writable).</param>
/// <param name="AcceptsDiscardChanges">True only for the headless host: <c>discardChanges</c> on new and open.</param>
public sealed record AgentPolicy(
    int DefaultLeaseSeconds,
    int MaxLeaseSeconds,
    bool AskFirst,
    ImmutableArray<string> ReadFolders,
    ImmutableArray<string> WriteFolders,
    string? ExportFolder,
    bool AcceptsDiscardChanges)
{
    /// <summary>The shortest lease in seconds.</summary>
    public const int MinLeaseSeconds = 15;

    /// <summary>The longest lease the settings allow in seconds.</summary>
    public const int MaxLeaseLimitSeconds = 600;

    /// <summary>The defaults: 90 s lease, 300 s maximum, no prompt, no extra folders.</summary>
    public static AgentPolicy Default { get; } = new(90, 300, false, [], [], null, false);

    /// <summary>Builds the policy from settings plus command line folders.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="extraRead">Folders from <c>--allow-read</c>.</param>
    /// <param name="extraWrite">Folders from <c>--allow-write</c>.</param>
    /// <param name="acceptsDiscardChanges">True for the headless host.</param>
    /// <returns>The policy.</returns>
    public static AgentPolicy FromSettings(
        AppSettings settings,
        IEnumerable<string>? extraRead = null,
        IEnumerable<string>? extraWrite = null,
        bool acceptsDiscardChanges = false)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var max = Math.Clamp(settings.AgentMaxLeaseSeconds, MinLeaseSeconds, MaxLeaseLimitSeconds);
        var lease = Math.Clamp(settings.AgentLeaseSeconds, MinLeaseSeconds, MaxLeaseLimitSeconds);
        return new AgentPolicy(
            Math.Min(lease, max),
            max,
            settings.AgentAskFirst,
            [.. (settings.AgentReadableFolders ?? []).Concat(extraRead ?? []).Where(f => !string.IsNullOrWhiteSpace(f))],
            [.. (settings.AgentWritableFolders ?? []).Concat(extraWrite ?? []).Where(f => !string.IsNullOrWhiteSpace(f))],
            string.IsNullOrWhiteSpace(settings.ExportFolder) ? null : settings.ExportFolder,
            acceptsDiscardChanges);
    }
}
