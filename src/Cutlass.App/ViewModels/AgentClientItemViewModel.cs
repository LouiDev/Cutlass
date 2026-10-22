using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services.Agent.Clients;

namespace Cutlass.App.ViewModels;

/// <summary>
/// One row of the client list (AGENT_INTEGRATION 9.4): the client's name, transport and where its registration lives,
/// its state (not installed, not registered, current, stale, foreign) and the actions that fit it: Register for a
/// client without an entry, Update for a stale or foreign one, Remove while an entry exists, and Copy (the setup text)
/// always. Text-only rows (harnesses, plain HTTP) only copy. The work runs through the owning
/// <see cref="ConnectClientsViewModel"/>. UI thread only.
/// </summary>
public sealed partial class AgentClientItemViewModel : ObservableObject
{
    private readonly ConnectClientsViewModel _owner;

    /// <summary>Creates the row.</summary>
    /// <param name="owner">The list that runs the actions.</param>
    /// <param name="profile">The client profile.</param>
    /// <param name="whereText">Where the registration lives: the configuration file, the CLI, or the setup text.</param>
    public AgentClientItemViewModel(ConnectClientsViewModel owner, AgentClientProfile profile, string whereText)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(whereText);
        _owner = owner;
        Profile = profile;
        WhereText = whereText;
        Scopes = profile.Scopes ?? [];
        SelectedScope = profile.DefaultScope.Length > 0 ? profile.DefaultScope : null;
        Probe = profile.IsTextOnly ? new AgentClientProbe(true, null, AgentClientState.Unknown) : AgentClientProbe.NotChecked;
    }

    /// <summary>The client profile.</summary>
    public AgentClientProfile Profile { get; }

    /// <summary>The profile id.</summary>
    public string Id => Profile.Id;

    /// <summary>The client's name.</summary>
    public string Name => Profile.Name;

    /// <summary>"MCP (stdio)" or "HTTP".</summary>
    public string TransportText => Profile.TransportText;

    /// <summary>True for rows that only copy their setup text.</summary>
    public bool IsTextOnly => Profile.IsTextOnly;

    /// <summary>Where the registration lives: the configuration file, "via the &lt;command&gt; CLI", or "copy the setup text".</summary>
    public string WhereText { get; }

    /// <summary>The scopes the client's CLI knows (empty for most clients).</summary>
    public IReadOnlyList<string> Scopes { get; }

    /// <summary>True when the row offers a scope choice (a CLI with scopes that can register now).</summary>
    public bool HasScopes => Scopes.Count > 1 && ShowPrimary;

    /// <summary>The scope Register uses (the profile's default first).</summary>
    [ObservableProperty]
    public partial string? SelectedScope { get; set; }

    /// <summary>The last check of this client.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(ToolTip), nameof(IsCurrent), nameof(IsWarning), nameof(IsDanger), nameof(IsInstalled))]
    [NotifyPropertyChangedFor(nameof(ShowPrimary), nameof(PrimaryText), nameof(ShowRemove), nameof(PrimaryToolTip), nameof(HasScopes))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(RemoveCommand))]
    public partial AgentClientProbe Probe { get; set; }

    /// <summary>True while an action of this row runs.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand), nameof(RemoveCommand), nameof(CopyCommand))]
    public partial bool IsBusy { get; set; }

    /// <summary>The state in words, or "copy only" for text rows.</summary>
    public string StateText => IsTextOnly ? "copy only" : Probe.Text;

    /// <summary>The registered command, the error, or where the registration lives.</summary>
    public string ToolTip => Probe.Detail is { Length: > 0 } detail ? detail : Probe.ConfigPath ?? WhereText;

    /// <summary>True when the entry starts this app's MCP exe (Success).</summary>
    public bool IsCurrent => Probe.State == AgentClientState.Current;

    /// <summary>True when the entry points elsewhere (stale or foreign; Warning).</summary>
    public bool IsWarning => Probe.State is AgentClientState.Stale or AgentClientState.Foreign;

    /// <summary>True when the check failed (Danger).</summary>
    public bool IsDanger => Probe.State == AgentClientState.Error;

    /// <summary>True when the client was detected (text rows count as available).</summary>
    public bool IsInstalled => IsTextOnly || Probe.Installed;

    /// <summary>True when Register or Update applies (an installed client without a current entry).</summary>
    public bool ShowPrimary => !IsTextOnly && Probe.State is AgentClientState.NotRegistered or AgentClientState.Stale or AgentClientState.Foreign;

    /// <summary>"Register" for a client without an entry, "Update" for a stale or foreign one.</summary>
    public string PrimaryText => Probe.State == AgentClientState.NotRegistered ? "Register" : "Update";

    /// <summary>What Register or Update does.</summary>
    public string PrimaryToolTip => Probe.State == AgentClientState.Foreign
        ? "Replace the cutlass entry, which Cutlass did not write (asks first)"
        : Profile.Kind == AgentClientKind.Cli
            ? "Runs the client's CLI: " + _owner.RegistrationText(this)
            : "Writes the cutlass entry into " + (Probe.ConfigPath ?? WhereText) + " (the old file is kept as .bak)";

    /// <summary>True while a <c>cutlass</c> entry exists.</summary>
    public bool ShowRemove => !IsTextOnly && Probe.Exists;

    private bool CanAct() => !IsBusy && !_owner.IsBusy;

    /// <summary>Register or Update.</summary>
    /// <returns>A task that completes when the client was registered or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task PrimaryAsync() => _owner.RegisterAsync(this);

    /// <summary>Remove the <c>cutlass</c> entry.</summary>
    /// <returns>A task that completes when the entry was removed or the user cancelled.</returns>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RemoveAsync() => _owner.RemoveAsync(this);

    /// <summary>Copy the setup text (the command, the configuration snippet or the harness text).</summary>
    /// <returns>A task that completes when the text is on the clipboard.</returns>
    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task CopyAsync() => _owner.CopyAsync(this);

    /// <summary>Refreshes the commands when the list starts or stops a job.</summary>
    internal void NotifyBusyChanged()
    {
        PrimaryCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
    }
}
