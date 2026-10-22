using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools;
using Cutlass.App.Services;
using Cutlass.App.Services.Agent;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The agent side of the main window (AGENT_INTEGRATION 6.2 and 6.3): the observable <see cref="AgentLockState"/>,
/// the lock banner (agent, reason, last call, elapsed time, open batch, Follow, Pause/Resume, Take over, the "ask
/// first" Allow and Deny, and the 3 s outcome after the lock ends), the status bar badge and the Agent panel (lock
/// card, connection card, call log). It never touches the lock itself: the buttons go through
/// <see cref="IAgentLockControls"/>, set by the agent host. UI thread only.
/// </summary>
public sealed partial class AgentViewModel : ObservableObject
{
    /// <summary>The most rows the call log keeps (the audit ring's size).</summary>
    public const int MaxCalls = AuditLog.DefaultCapacity;

    /// <summary>How long the banner shows the outcome after the lock ended.</summary>
    public const long OutcomeMs = 3000;

    private readonly Func<long> _nowMs;
    private readonly IDialogService? _dialogs;
    private long _outcomeUntilMs;

    /// <summary>Creates the agent state with the lock free.</summary>
    /// <param name="nowMs">The clock in Unix milliseconds (the lock driver's clock).</param>
    /// <param name="dialogs">Clipboard and file opening for the panel, or null.</param>
    public AgentViewModel(Func<long> nowMs, IDialogService? dialogs = null)
    {
        ArgumentNullException.ThrowIfNull(nowMs);
        _nowMs = nowMs;
        _dialogs = dialogs;
        State = AgentLockState.Free;
        Follow = true;
        UpdateTexts();
    }

    /// <summary>Raised when the lock starts or stops blocking the user (<see cref="IsLocked"/> changed).</summary>
    public event EventHandler? LockedChanged;

    /// <summary>Raised when the badge or the View menu asks for the AGENT tab.</summary>
    public event EventHandler? ShowPanelRequested;

    /// <summary>The banner and panel buttons; set by the agent host. While null the buttons do nothing.</summary>
    public IAgentLockControls? Controls
    {
        get;
        set
        {
            field = value;
            NotifyCommands();
        }
    }

    /// <summary>The server for the connection card (address, token, MCP paths), or null.</summary>
    public IAgentSettingsHost? Server
    {
        get;
        set
        {
            if (field is not null)
            {
                field.StatusChanged -= OnServerStatusChanged;
            }

            field = value;
            if (value is not null)
            {
                value.StatusChanged += OnServerStatusChanged;
            }

            UpdateServer();
        }
    }

    /// <summary>The log folder for "Open log folder", or null.</summary>
    public string? LogFolder { get; set; }

    /// <summary>The lock state (the agent host updates it after every transition).</summary>
    [ObservableProperty]
    public partial AgentLockState State { get; private set; }

    /// <summary>True in Held and Paused: the editing UI is read-only.</summary>
    [ObservableProperty]
    public partial bool IsLocked { get; private set; }

    /// <summary>True while an agent holds the lease and runs.</summary>
    [ObservableProperty]
    public partial bool IsHeld { get; private set; }

    /// <summary>True while paused.</summary>
    [ObservableProperty]
    public partial bool IsPaused { get; private set; }

    /// <summary>True while an agent waits for the project.</summary>
    [ObservableProperty]
    public partial bool IsPending { get; private set; }

    /// <summary>True while the "ask first" prompt waits for Allow or Deny.</summary>
    [ObservableProperty]
    public partial bool IsAsking { get; private set; }

    /// <summary>True while the banner shows (held, paused, pending, asking, or the outcome for 3 s).</summary>
    [ObservableProperty]
    public partial bool IsBannerVisible { get; private set; }

    /// <summary>True while the banner shows the outcome of a lock that ended.</summary>
    [ObservableProperty]
    public partial bool IsShowingOutcome { get; private set; }

    /// <summary>True when the banner shows Follow, Pause/Resume and Take over (held or paused).</summary>
    public bool ShowBannerControls => IsLocked;

    /// <summary>The bold agent name at the start of the banner.</summary>
    [ObservableProperty]
    public partial string AgentNameText { get; private set; } = string.Empty;

    /// <summary>The rest of the banner headline, for example "is editing: Remove silences, add title".</summary>
    [ObservableProperty]
    public partial string HeadlineText { get; private set; } = string.Empty;

    /// <summary>The last tool and its age, for example "split, 2 s ago".</summary>
    [ObservableProperty]
    public partial string LastCallText { get; private set; } = string.Empty;

    /// <summary>The time since the grant, for example "1:05".</summary>
    [ObservableProperty]
    public partial string ElapsedText { get; private set; } = string.Empty;

    /// <summary>The open batch, for example "Batch: Remove silences", or empty.</summary>
    [ObservableProperty]
    public partial string BatchText { get; private set; } = string.Empty;

    /// <summary>The lease left, for example "Lease 1:12 left" or "Lease paused, 0:40 left".</summary>
    [ObservableProperty]
    public partial string LeaseText { get; private set; } = string.Empty;

    /// <summary>"Pause" or "Resume".</summary>
    [ObservableProperty]
    public partial string PauseResumeText { get; private set; } = "Pause";

    /// <summary>Follow: after each agent edit select and reveal it, and show the frames it looks at (unless playing).</summary>
    [ObservableProperty]
    public partial bool Follow { get; set; }

    /// <summary>The phase in words for the panel's lock card, for example "Held by Claude Code".</summary>
    [ObservableProperty]
    public partial string PhaseText { get; private set; } = string.Empty;

    /// <summary>The status bar text while an agent waits ("Claude Code is waiting for the project"), else empty.</summary>
    [ObservableProperty]
    public partial string PendingText { get; private set; } = string.Empty;

    /// <summary>True when agent connections are on (the badge and the AGENT tab show).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBadgeVisible))]
    public partial bool IsEnabled { get; set; }

    /// <summary>True when the status bar badge shows (connections on, or an agent still holds the project).</summary>
    public bool IsBadgeVisible => IsEnabled || IsLocked || IsPending;

    /// <summary>The badge: "Agent ready", "Agent: Claude Code", "Agent paused" or "Agent waiting".</summary>
    [ObservableProperty]
    public partial string BadgeText { get; private set; } = "Agent ready";

    /// <summary>The badge dot is Accent (an agent holds the project).</summary>
    [ObservableProperty]
    public partial bool BadgeIsAccent { get; private set; }

    /// <summary>The badge dot is amber (paused).</summary>
    [ObservableProperty]
    public partial bool BadgeIsAmber { get; private set; }

    /// <summary>The badge dot is TextSecondary (ready or waiting).</summary>
    [ObservableProperty]
    public partial bool BadgeIsIdle { get; private set; } = true;

    /// <summary>The server's address, for example <c>http://localhost:47390/</c>, or the status when it does not listen.</summary>
    [ObservableProperty]
    public partial string ServerText { get; private set; } = "Off";

    /// <summary>True while the server listens.</summary>
    [ObservableProperty]
    public partial bool IsServerListening { get; private set; }

    /// <summary>The note after a panel action ("Copied"), or empty.</summary>
    [ObservableProperty]
    public partial string PanelNote { get; private set; } = string.Empty;

    /// <summary>The call log, newest first, at most <see cref="MaxCalls"/> rows.</summary>
    public ObservableCollection<AgentCallItemViewModel> Calls { get; } = [];

    /// <summary>True when the call log has rows (the empty hint hides).</summary>
    [ObservableProperty]
    public partial bool HasCalls { get; private set; }

    /// <summary>Formats milliseconds as <c>m:ss</c> (or <c>h:mm:ss</c>), never negative.</summary>
    /// <param name="ms">The milliseconds.</param>
    /// <returns>The text.</returns>
    public static string FormatDuration(long ms)
    {
        var total = Math.Max(0, ms) / 1000;
        var hours = total / 3600;
        var minutes = total / 60 % 60;
        var seconds = total % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }

    /// <summary>Formats an age in seconds, for example "2 s ago" or "3 min ago".</summary>
    /// <param name="ms">The age in milliseconds.</param>
    /// <returns>The text.</returns>
    public static string FormatAge(long ms)
    {
        var seconds = Math.Max(0, ms) / 1000;
        return seconds < 120
            ? string.Create(CultureInfo.InvariantCulture, $"{seconds} s ago")
            : string.Create(CultureInfo.InvariantCulture, $"{seconds / 60} min ago");
    }

    /// <summary>The banner's outcome line after a lock ended (AGENT_INTEGRATION 6.2).</summary>
    /// <param name="reason">How it ended.</param>
    /// <param name="agent">The agent.</param>
    /// <returns>"Claude Code finished", "You took over" or "Claude Code timed out".</returns>
    public static string OutcomeText(LockLostReason reason, string? agent)
    {
        var name = string.IsNullOrWhiteSpace(agent) ? "The agent" : agent;
        return reason switch
        {
            LockLostReason.RevokedByUser => "You took over",
            LockLostReason.Expired => name + " timed out",
            _ => name + " finished",
        };
    }

    /// <summary>Takes a new lock state (UI thread): updates the banner, the badge and the read-only switch.</summary>
    /// <param name="state">The state.</param>
    public void Apply(AgentLockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var wasLocked = State.IsHeld;
        State = state;
        if (wasLocked && !state.IsHeld && state.EndedReason is not null)
        {
            _outcomeUntilMs = _nowMs() + OutcomeMs;
        }
        else if (state.IsHeld)
        {
            _outcomeUntilMs = 0;
        }

        UpdateTexts();
        NotifyCommands();
        if (wasLocked != state.IsHeld)
        {
            LockedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Refreshes the time texts and ends the outcome display (called every second by the agent host).</summary>
    public void Refresh() => UpdateTexts();

    /// <summary>Adds a call log row (UI thread), newest first.</summary>
    /// <param name="entry">The audit entry.</param>
    public void AddCall(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Calls.Insert(0, new AgentCallItemViewModel(entry));
        while (Calls.Count > MaxCalls)
        {
            Calls.RemoveAt(Calls.Count - 1);
        }

        HasCalls = true;
    }

    /// <summary>Replaces the call log (UI thread), for example from the audit ring when the panel first shows.</summary>
    /// <param name="newestFirst">The entries, newest first.</param>
    public void LoadCalls(IReadOnlyList<AuditEntry> newestFirst)
    {
        ArgumentNullException.ThrowIfNull(newestFirst);
        Calls.Clear();
        foreach (var entry in newestFirst.Take(MaxCalls))
        {
            Calls.Add(new AgentCallItemViewModel(entry));
        }

        HasCalls = Calls.Count > 0;
    }

    partial void OnIsLockedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBannerControls));
        OnPropertyChanged(nameof(IsBadgeVisible));
    }

    partial void OnIsPendingChanged(bool value) => OnPropertyChanged(nameof(IsBadgeVisible));

    private bool CanTakeOver() => IsLocked && Controls is not null;

    private bool CanAnswer() => IsAsking && Controls is not null;

    /// <summary>Take over (banner and panel).</summary>
    [RelayCommand(CanExecute = nameof(CanTakeOver))]
    private void TakeOver() => Controls?.TakeOver();

    /// <summary>Pause or Resume (banner and panel).</summary>
    [RelayCommand(CanExecute = nameof(CanTakeOver))]
    private void PauseResume()
    {
        if (Controls is not { } controls)
        {
            return;
        }

        if (IsPaused)
        {
            controls.Resume();
        }
        else
        {
            controls.Pause();
        }
    }

    /// <summary>Toggles Follow (the command id for menus; the banner binds <see cref="Follow"/>).</summary>
    [RelayCommand]
    private void ToggleFollow() => Follow = !Follow;

    /// <summary>"Allow" on the "ask first" prompt.</summary>
    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private void Allow() => Controls?.Answer(true);

    /// <summary>"Deny" on the "ask first" prompt.</summary>
    [RelayCommand(CanExecute = nameof(CanAnswer))]
    private void Deny() => Controls?.Answer(false);

    /// <summary>The badge click and View > Agent panel: shows the AGENT tab.</summary>
    [RelayCommand]
    private void ShowPanel() => ShowPanelRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task CopyTokenAsync()
    {
        if (Server is not { } server || _dialogs is null)
        {
            return;
        }

        try
        {
            await _dialogs.CopyToClipboardAsync(await server.ReadTokenAsync(CancellationToken.None));
            PanelNote = "Token copied.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PanelNote = "Could not read the token: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task CopyClaudeCommandAsync()
    {
        if (Server is not { } server || _dialogs is null)
        {
            return;
        }

        await _dialogs.CopyToClipboardAsync("claude mcp add cutlass -- " + SettingsViewModel.QuoteIfNeeded(server.McpExecutablePath));
        PanelNote = "Claude Code command copied.";
    }

    [RelayCommand]
    private Task OpenDocsAsync() =>
        Server is { } server && _dialogs is not null ? _dialogs.OpenFileAsync(server.AgentDocsPath) : Task.CompletedTask;

    [RelayCommand]
    private Task OpenLogFolderAsync() =>
        LogFolder is { } folder && _dialogs is not null ? _dialogs.RevealInFileManagerAsync(folder) : Task.CompletedTask;

    private void OnServerStatusChanged(object? sender, EventArgs e) => UpdateServer();

    private void UpdateServer()
    {
        if (Server is not { } server)
        {
            ServerText = "Off";
            IsServerListening = false;
            return;
        }

        ServerText = server.StatusText;
        IsServerListening = server.Status == AgentServerStatus.Listening;
    }

    private void NotifyCommands()
    {
        TakeOverCommand.NotifyCanExecuteChanged();
        PauseResumeCommand.NotifyCanExecuteChanged();
        AllowCommand.NotifyCanExecuteChanged();
        DenyCommand.NotifyCanExecuteChanged();
    }

    private void UpdateTexts()
    {
        var state = State;
        var now = _nowMs();
        IsLocked = state.IsHeld;
        IsHeld = state.Phase == LockPhase.Held;
        IsPaused = state.Phase == LockPhase.Paused;
        IsPending = state.Phase == LockPhase.Pending;
        IsAsking = state.AskingUser;
        IsShowingOutcome = !state.IsHeld && _outcomeUntilMs > now && state.EndedReason is not null;
        IsBannerVisible = IsLocked || IsPending || IsAsking || IsShowingOutcome;
        PauseResumeText = IsPaused ? "Resume" : "Pause";

        if (state.IsHeld)
        {
            AgentNameText = state.AgentName ?? "An agent";
            HeadlineText = string.IsNullOrWhiteSpace(state.Reason) ? "is editing" : "is editing: " + state.Reason;
            LastCallText = state.LastTool is { } tool && state.LastToolAtMs is { } at
                ? tool + ", " + FormatAge(now - at)
                : "no calls yet";
            ElapsedText = state.SinceMs is { } since ? FormatDuration(now - since) : string.Empty;
            BatchText = state.BatchLabel is { Length: > 0 } batch ? "Batch: " + batch : string.Empty;
            LeaseText = IsPaused
                ? "Lease paused, " + FormatDuration(state.PausedRemainingMs ?? 0) + " left"
                : state.LeaseExpiresAtMs is { } expiry ? "Lease " + FormatDuration(expiry - now) + " left" : string.Empty;
            PhaseText = (IsPaused ? "Paused: " : "Held by ") + AgentNameText;
        }
        else if (IsAsking || IsPending)
        {
            AgentNameText = state.PendingAgent ?? "An agent";
            HeadlineText = IsAsking
                ? "wants to edit" + (string.IsNullOrWhiteSpace(state.PendingReason) ? string.Empty : ": " + state.PendingReason)
                : "is waiting for the project" + (string.IsNullOrWhiteSpace(state.PendingReason) ? string.Empty : ": " + state.PendingReason);
            LastCallText = ElapsedText = BatchText = LeaseText = string.Empty;
            PhaseText = AgentNameText + " is waiting";
        }
        else if (IsShowingOutcome)
        {
            AgentNameText = string.Empty;
            HeadlineText = OutcomeText(state.EndedReason!.Value, state.EndedAgent);
            LastCallText = ElapsedText = BatchText = LeaseText = string.Empty;
            PhaseText = "Free";
        }
        else
        {
            AgentNameText = HeadlineText = LastCallText = ElapsedText = BatchText = LeaseText = string.Empty;
            PhaseText = "Free: you hold the project";
        }

        PendingText = IsPending || IsAsking ? AgentNameText + " is waiting for the project" : string.Empty;

        BadgeIsAccent = IsHeld;
        BadgeIsAmber = IsPaused;
        BadgeIsIdle = !IsHeld && !IsPaused;
        BadgeText = IsHeld ? "Agent: " + (state.AgentName ?? "agent")
            : IsPaused ? "Agent paused"
            : IsPending ? "Agent waiting"
            : "Agent ready";
    }
}
