using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Cutlass.App.Services;
using Cutlass.App.Services.Agent;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Core.Serialization;
using Cutlass.Core.Session;
using Cutlass.Media.Export;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The agent side of the main window (AGENT_INTEGRATION 3.7 to 3.9 and 6): <see cref="Agent"/> holds the lock state
/// for the banner, badge and panel; while an agent holds or pauses the project the command gate lets only
/// <see cref="CommandIds.AllowedWhileAgentHolds"/> through, the timeline, inspector, bin and Effects tab turn
/// read-only, and a write the session's gate refuses anyway shows "An agent is editing. Take over to edit." (at most
/// every 3 s). It also offers the host operations the agent host needs (import, new, open, save, export) with the
/// same paths the menus use, minus their prompts.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>The toast of a key or write refused while an agent holds the project (AGENT_INTEGRATION 3.7).</summary>
    public const string AgentBlockedMessage = "An agent is editing. Take over to edit.";

    private const long BlockedToastIntervalMs = 3000;

    private readonly HashSet<ExportJob> _userExports = [];
    private long? _lastBlockedToastMs;

    /// <summary>
    /// Raised (posted to the next dispatcher turn) when something the agent lock asks about changed: a gesture ended, a
    /// dialog opened or closed, an export finished, playback started or stopped (AGENT_INTEGRATION 2.4).
    /// </summary>
    public event EventHandler? AgentUserStateChanged;

    /// <summary>Raised on grant: the view moves the keyboard focus away so inline editors commit as on focus loss.</summary>
    public event EventHandler? FocusResetRequested;

    /// <summary>The agent lock state, banner, badge and panel.</summary>
    public AgentViewModel Agent { get; }

    /// <summary>Edit > Undo with the step it undoes, for example "Undo Agent: Remove silences" (AGENT_INTEGRATION 1.1).</summary>
    [ObservableProperty]
    public partial string UndoMenuText { get; private set; } = "_Undo";

    /// <summary>Edit > Redo with the step it redoes.</summary>
    [ObservableProperty]
    public partial string RedoMenuText { get; private set; } = "_Redo";

    /// <summary>A menu header for an undo or redo step: the access key, then the label with its underscores doubled.</summary>
    /// <param name="verb">"Undo" or "Redo".</param>
    /// <param name="label">The step's label, or null.</param>
    /// <returns>The header, for example "_Undo Agent: Add Title".</returns>
    public static string HistoryMenuText(string verb, string? label) =>
        "_" + verb + (string.IsNullOrWhiteSpace(label) ? string.Empty : " " + label.Replace("_", "__", StringComparison.Ordinal));

    /// <summary>How often the session's write gate refused a user write (m9 asserts it never does).</summary>
    public int AgentWriteRefusedCount { get; private set; }

    /// <summary>True while a project is open and no agent holds it (the + Track button and project edits).</summary>
    public bool CanEditProject => HasProject && !Agent.IsLocked;

    /// <summary>True while a user-started export is queued or running (agent exports do not count).</summary>
    public bool IsUserExportRunning => _userExports.Any(j => !j.Completion.IsCompleted);

    /// <summary>True while either monitor plays back.</summary>
    public bool IsAnyMonitorPlaying => Viewer.Playback.IsPlaying || Viewer.Source.Playback.IsPlaying;

    /// <summary>The agent server's lifetime (stopped before the services when the window closes), or null.</summary>
    public IAsyncDisposable? AgentRuntime { get; set; }

    /// <summary>The agent server for the Settings > Agents page and the panel, or null.</summary>
    public IAgentSettingsHost? AgentServer
    {
        get => Agent.Server;
        set => Agent.Server = value;
    }

    /// <summary>
    /// The command gate (AGENT_INTEGRATION 3.7): every command while the lock is free; while an agent holds or pauses
    /// the project only the allowed set, plus the routed mark commands while they target the Source monitor.
    /// </summary>
    /// <param name="id">The command id.</param>
    /// <returns>True when the command may run.</returns>
    public bool IsCommandAllowed(string id) =>
        !Agent.IsLocked
        || CommandIds.AllowedWhileAgentHolds.Contains(id)
        || (CommandIds.RoutedToSource.Contains(id) && Viewer.IsSourceActive);

    /// <summary>Shows the "agent is editing" toast, at most every 3 s.</summary>
    public void ShowAgentBlockedToast()
    {
        var now = Environment.TickCount64;
        if (_lastBlockedToastMs is { } last && now - last < BlockedToastIntervalMs)
        {
            return;
        }

        _lastBlockedToastMs = now;
        ShowToast(AgentBlockedMessage);
    }

    /// <summary>On grant: inline editors close as focus loss does (they commit a valid value), the timecode entry ends.</summary>
    public void PrepareForAgent()
    {
        Viewer.CancelTimecodeEdit();

        // The marker editor and the rename box are canvas overlays that clearing focus alone leaves open.
        Timeline.CloseInlineEditors();
        FocusResetRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Follow: selects what an agent edit names and scrolls it into view (AGENT_INTEGRATION 6.2).</summary>
    /// <param name="result">The edit's result.</param>
    public void FollowAgentEdit(EditResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!Agent.Follow || result.Select.IsDefaultOrEmpty)
        {
            return;
        }

        Timeline.Select(result.Select, additive: false);
        var project = Session.Current;
        var starts = result.Select.Select(id => TimelineQuery.FindClip(project, id)?.Clip.Start).OfType<long>().ToList();
        if (starts.Count > 0 && !IsAnyMonitorPlaying)
        {
            Timeline.ViewState.EnsureVisible(starts.Min());
        }
    }

    /// <summary>Follow: moves the playhead to the frame the agent looks at, unless a monitor plays (AGENT_INTEGRATION 3.8).</summary>
    /// <param name="frame">The frame.</param>
    public void FollowAgentFocus(long frame)
    {
        if (Agent.Follow && !IsAnyMonitorPlaying && HasProject)
        {
            Timeline.Playhead = Math.Max(0, frame);
        }
    }

    /// <summary>
    /// Imports files for an agent through the media bin (placeholders, posters, media jobs), committing each as the
    /// agent after <paramref name="mayCommit"/> said yes. Starts an untitled project when none is open.
    /// </summary>
    /// <param name="paths">The files.</param>
    /// <param name="mayCommit">Checked right before each commit (the agent still holds its lock).</param>
    /// <returns>The imported assets and the report.</returns>
    public async Task<(IReadOnlyList<MediaAsset> Imported, MediaImportReport Report)> ImportForAgentAsync(IReadOnlyList<string> paths, Func<bool> mayCommit)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(mayCommit);
        if (!HasProject)
        {
            using (Session.WriteAs(SessionWriter.Agent))
            {
                StartProject(TimelineEdits.NewProject(_services.Ids), null);
            }
        }

        var report = new MediaImportReport { Writer = SessionWriter.Agent, MayCommit = mayCommit };
        var imported = await MediaBin.ImportPathsAsync(paths, report);
        return (imported, report);
    }

    /// <summary>
    /// Replaces the project for an agent (new or opened), without the save prompt (the caller refuses a dirty project)
    /// and without the recovery prompt. The project is loaded and scanned first; <paramref name="mayCommit"/> is
    /// checked right before it becomes current.
    /// </summary>
    /// <param name="path">The project file, or null for a new untitled project.</param>
    /// <param name="settings">The sequence settings of a new project, or null for the defaults.</param>
    /// <param name="mayCommit">Checked right before the replace (the agent still holds its lock).</param>
    /// <returns>Null on success, else the error message; and whether the commit check failed.</returns>
    public async Task<(string? Error, bool LockLost)> ReplaceProjectForAgentAsync(string? path, SequenceSettings? settings, Func<bool> mayCommit)
    {
        ArgumentNullException.ThrowIfNull(mayCommit);
        Project project;
        if (path is null)
        {
            project = TimelineEdits.NewProject(_services.Ids);
            if (settings is not null)
            {
                project = TimelineEdits.SetSequenceSettings(project, settings).Project;
            }

            _pendingScan = null;
        }
        else
        {
            try
            {
                _openingRecovered = false;
                project = await ScanForOpenAsync(await _services.Projects.LoadAsync(path), path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                return (System.IO.Path.GetFileName(path) + " could not be opened. " + ex.Message, false);
            }
        }

        if (!mayCommit())
        {
            _pendingScan = null;
            return (null, true);
        }

        ViewState? view = null;
        if (path is not null)
        {
            view = await _services.Projects.LoadViewStateAsync(path);
            if (!mayCommit())
            {
                _pendingScan = null;
                return (null, true);
            }
        }

        await SaveViewStateAsync();
        using (Session.WriteAs(SessionWriter.Agent))
        {
            StartProject(project, path);
        }

        if (path is not null && view is not null)
        {
            ApplyViewState(view);
            await AddRecentAsync(path);
        }

        return (null, false);
    }

    /// <summary>Saves for an agent: the same as Save (sidecar, recent list, autosave cleared) but reports instead of a dialog.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Null on success, else the error message.</returns>
    public Task<string?> SaveForAgentAsync(string path) => SaveToPathCoreAsync(path);

    /// <summary>Queues an export for an agent; it shows in the Jobs popover but never counts as the user's export.</summary>
    /// <param name="request">The export.</param>
    /// <returns>The job, or null when the export service is unavailable.</returns>
    public ExportJob? StartExportForAgent(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return StartExportCore(request, byAgent: true);
    }

    // Called once from the constructor.
    private void InitializeAgent()
    {
        Commands.Gate = IsCommandAllowed;
        Keys.CommandBlocked += (_, _) => ShowAgentBlockedToast();
        Session.WriteRefused += (_, label) =>
        {
            AgentWriteRefusedCount++;
            _log?.Write("agent: the write gate refused the user's " + label);
            ShowAgentBlockedToast();
        };
        Agent.LockedChanged += (_, _) => ApplyAgentLock();
        Agent.ShowPanelRequested += (_, _) => MediaBin.ShowAgentTabPageCommand.Execute(null);
        Agent.LogFolder = _services.LogFolder;
        MediaBin.Agent = Agent;
        Agent.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AgentViewModel.IsEnabled))
            {
                MediaBin.ShowAgentTab = Agent.IsEnabled || Agent.IsLocked;
            }
        };

        Commands.Register(CommandIds.AgentTakeOver, Agent.TakeOverCommand);
        Commands.Register(CommandIds.AgentPauseResume, Agent.PauseResumeCommand);
        Commands.Register(CommandIds.AgentToggleFollow, Agent.ToggleFollowCommand);
        Commands.Register(CommandIds.ShowAgentPanel, Agent.ShowPanelCommand);

        // The user state the lock asks about (AGENT_INTEGRATION 2.4); grants happen in the dispatcher turn after.
        Session.Changed += (_, e) =>
        {
            // Agent batches commit as GroupCommit or GroupCancel; like gestures, only their ends change the history.
            if (e.Kind is ChangeKind.GestureCommit or ChangeKind.GestureCancel)
            {
                RaiseAgentUserStateChanged();
            }

            UpdateHistoryMenu();
        };
        UpdateHistoryMenu();
        Timeline.PropertyChanged += OnTimelineDraggingChanged;
        Viewer.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewerViewModel.IsPlaying))
            {
                RaiseAgentUserStateChanged();
            }
        };
        if (_dialogs is TrackedDialogService tracked)
        {
            tracked.DialogsChanged += (_, _) => RaiseAgentUserStateChanged();
        }
    }

    private void OnTimelineDraggingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TimelineViewModel.IsDragging) && !Timeline.IsDragging)
        {
            RaiseAgentUserStateChanged();
        }
    }

    private void UpdateHistoryMenu()
    {
        UndoMenuText = HistoryMenuText("Undo", Session.CanUndo ? Session.UndoLabel : null);
        RedoMenuText = HistoryMenuText("Redo", Session.CanRedo ? Session.RedoLabel : null);
    }

    private void RaiseAgentUserStateChanged() => _post(() => AgentUserStateChanged?.Invoke(this, EventArgs.Empty));

    // The lock started or stopped blocking the user: every editing surface follows at once (same dispatcher turn).
    private void ApplyAgentLock()
    {
        var locked = Agent.IsLocked;
        Timeline.IsReadOnly = locked;
        Inspector.IsReadOnly = locked;
        MediaBin.IsReadOnly = locked;
        EffectsTab.IsReadOnly = locked;
        MediaBin.ShowAgentTab = Agent.IsEnabled || locked;
        OnPropertyChanged(nameof(CanEditProject));
        RunCommandCommand.NotifyCanExecuteChanged();
        NewCommand.NotifyCanExecuteChanged();
        OpenCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
    }

    private bool CanReplaceProject() => !Agent.IsLocked;

    private bool CanRunCommand(string? id) => id is null || Commands.IsAllowed(id);

    private void ForgetUserExport(ExportJob job)
    {
        if (_userExports.Remove(job))
        {
            RaiseAgentUserStateChanged();
        }
    }
}
