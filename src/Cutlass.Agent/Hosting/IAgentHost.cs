using Cutlass.Agent.Lock;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Session;
using Cutlass.Media.Export;

namespace Cutlass.Agent.Hosting;

/// <summary>
/// What the agent tools see of their host (AGENT_INTEGRATION 2.2): the App or the headless host. Tools never see
/// Avalonia, view models or <c>AppServices</c>. Members marked "UI thread only" must be used through <see cref="Ui"/>.
/// </summary>
public interface IAgentHost
{
    /// <summary><c>app</c> or <c>headless</c>.</summary>
    string Kind { get; }

    /// <summary>Marshals onto the host's UI thread.</summary>
    IAgentUiInvoker Ui { get; }

    /// <summary>The thread-safe media services.</summary>
    IAgentServices Services { get; }

    /// <summary>An immutable snapshot of the agent settings; replaced when settings change. Any thread.</summary>
    AgentPolicy Policy { get; }

    /// <summary>The session. UI thread only.</summary>
    ProjectSession Session { get; }

    /// <summary>The agent lock. UI thread only.</summary>
    AgentLock Lock { get; }

    /// <summary>The project file path, or null while untitled. UI thread only.</summary>
    string? ProjectPath { get; }

    /// <summary>The user's playhead (App: the timeline playhead; headless: a field). UI thread only.</summary>
    long Playhead { get; set; }

    /// <summary>What the user is doing right now. UI thread only.</summary>
    AgentUserState UserState { get; }

    /// <summary>Raised on the UI thread when <see cref="UserState"/> changed (a gesture ended, a dialog closed, an export finished).</summary>
    event EventHandler? UserStateChanged;

    /// <summary>On grant: close inline editors as focus loss does. UI thread only.</summary>
    void PrepareForAgent();

    /// <summary>Follow: select and reveal what an agent edit changed. UI thread only.</summary>
    /// <param name="result">The edit's result.</param>
    void OnAgentEdit(EditResult result);

    /// <summary>Follow: move the playhead to what the agent looks at, unless playing. UI thread only.</summary>
    /// <param name="frame">The frame.</param>
    void OnAgentFocus(long frame);

    /// <summary>Called after every lock transition. UI thread only.</summary>
    /// <param name="state">The new state.</param>
    void OnLockChanged(AgentLockState state);

    /// <summary>
    /// Imports files into the bin (no placement) the way the host's media bin does. Any thread; it marshals itself and
    /// rechecks the lock at its commit point (throwing <see cref="Api.ApiException"/> with <c>lock_lost</c> when it
    /// changed).
    /// </summary>
    /// <param name="paths">The files (already checked against the read folders).</param>
    /// <param name="lockId">The caller's lock id.</param>
    /// <param name="ct">Cancels the import.</param>
    /// <returns>What was imported and what failed.</returns>
    Task<HostImport> ImportAsync(IReadOnlyList<string> paths, string lockId, CancellationToken ct);

    /// <summary>Replaces the project with a new untitled one. Any thread.</summary>
    /// <param name="settings">The sequence settings, or null for the defaults.</param>
    /// <param name="lockId">The caller's lock id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    Task<HostOutcome> NewProjectAsync(SequenceSettings? settings, string lockId, CancellationToken ct);

    /// <summary>Opens a project file (relink scan, sidecar). Any thread.</summary>
    /// <param name="path">The project file (already checked against the read folders).</param>
    /// <param name="lockId">The caller's lock id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The outcome.</returns>
    Task<HostOutcome> OpenProjectAsync(string path, string lockId, CancellationToken ct);

    /// <summary>Saves the project, to <paramref name="path"/> when given (save as). Any thread.</summary>
    /// <param name="path">The new path (already checked against the write folders), or null for the current path.</param>
    /// <param name="lockId">The caller's lock id.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The outcome with the saved path.</returns>
    Task<HostOutcome> SaveProjectAsync(string? path, string lockId, CancellationToken ct);

    /// <summary>Queues an export of the request's snapshot where the host shows exports (the App's Jobs popover). Any thread.</summary>
    /// <param name="request">The export.</param>
    /// <param name="lockId">The caller's lock id.</param>
    /// <param name="ct">Cancels the call (not the export).</param>
    /// <returns>The job.</returns>
    Task<ExportJob> StartExportAsync(ExportRequest request, string lockId, CancellationToken ct);
}
