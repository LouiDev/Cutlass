using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cutlass.App.Services;
using Cutlass.App.Services.Agent.Clients;

namespace Cutlass.App.ViewModels;

/// <summary>
/// The agent client list (AGENT_INTEGRATION 9.4) that Agent > Connect an agent client..., Settings > Agents > Connect
/// clients, the Agent panel's "Clients" line and the first-start dialog share: one row per profile (detected clients
/// first, then the others, then the text-only rows), each with its state and Register / Update / Remove or Copy, the
/// output of the last action, the token note and the discovery URLs of the server. Every check, CLI run and file write
/// goes through <see cref="AgentClientRegistry"/> off the UI thread; results are applied on the calling (UI) thread.
/// A replaced foreign entry, a removal and a copied text with the token are confirmed first. Never touches controls.
/// </summary>
public sealed partial class ConnectClientsViewModel : ObservableObject
{
    /// <summary>The note about the token under the list.</summary>
    public const string TokenNote =
        "Clients that call the HTTP API need the agent token. Copy asks before it puts the token into the text, and a registration for an HTTP transport carries the token in that client's configuration. MCP registrations need no token.";

    private readonly IDialogService _dialogs;
    private readonly Func<AgentClientContext> _context;
    private readonly Func<Task<string?>> _readToken;
    private Task? _load;
    private Task? _refresh;
    private string? _probedFor;
    private bool _probed;

    /// <summary>Creates the list.</summary>
    /// <param name="registry">The client registry.</param>
    /// <param name="dialogs">Confirmations and the clipboard.</param>
    /// <param name="context">The live values (MCP exe, server URL, token file, the folder a CLI runs in).</param>
    /// <param name="readToken">Reads the agent token after the user confirmed, or null when it cannot be read.</param>
    public ConnectClientsViewModel(AgentClientRegistry registry, IDialogService dialogs, Func<AgentClientContext> context, Func<Task<string?>> readToken)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(readToken);
        Registry = registry;
        _dialogs = dialogs;
        _context = context;
        _readToken = readToken;
    }

    /// <summary>Raised on the UI thread when the dialog's Close was pressed.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised on the UI thread after a client was registered (the profile id).</summary>
    public event EventHandler<string>? Registered;

    /// <summary>The client registry.</summary>
    public AgentClientRegistry Registry { get; }

    /// <summary>The rows: detected clients first, then the others, then the text-only rows.</summary>
    public ObservableCollection<AgentClientItemViewModel> Items { get; } = [];

    /// <summary>The server's discovery URLs with Copy (Settings > Agents).</summary>
    public ObservableCollection<AgentDiscoveryUrlItem> DiscoveryUrls { get; } = [];

    /// <summary>The window title of the dialog.</summary>
    public static string Title => "Connect an agent client";

    /// <summary>The explanation at the top of the dialog.</summary>
    public static string IntroText =>
        "An MCP client starts the Cutlass MCP server itself once it knows it; other harnesses call the HTTP API. Register writes the client's own configuration (its CLI, or its configuration file with a .bak copy); Copy puts the same setup on the clipboard to do it by hand.";

    /// <summary>The token note (for binding).</summary>
    public static string TokenNoteText => TokenNote;

    /// <summary>True while the list checks the clients.</summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; private set; }

    /// <summary>True while a Register, Update or Remove runs (the other rows wait).</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>How many clients start this app's MCP exe.</summary>
    [ObservableProperty]
    public partial int CurrentCount { get; private set; }

    /// <summary>"3 current", "none current", "checking..." or "not checked" (the panel's "Clients" line).</summary>
    [ObservableProperty]
    public partial string SummaryText { get; private set; } = "not checked";

    /// <summary>The detected clients and their states, for the panel's tooltip.</summary>
    [ObservableProperty]
    public partial string SummaryToolTip { get; private set; } = string.Empty;

    /// <summary>What the last action ran or wrote, or a note; empty before.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutput))]
    public partial string OutputText { get; private set; } = string.Empty;

    /// <summary>True when the output box shows.</summary>
    public bool HasOutput => OutputText.Length > 0;

    /// <summary>The live values for a registration (no token).</summary>
    /// <returns>The context.</returns>
    public AgentClientContext Context() => _context();

    /// <summary>The setup text of a row (the command, the configuration snippet or the harness text), without the token.</summary>
    /// <param name="item">The row.</param>
    /// <returns>The text.</returns>
    public string RegistrationText(AgentClientItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Registry.RegistrationText(item.Profile, _context(), item.SelectedScope);
    }

    /// <summary>Loads the profiles once (off the UI thread) and creates the rows and the discovery URLs.</summary>
    /// <returns>A task that completes when the rows exist.</returns>
    public Task LoadAsync() => _load ??= LoadCoreAsync();

    /// <summary>
    /// Checks every client (not the text rows) off the UI thread and sorts the rows. A check of the same working
    /// directory is reused unless <paramref name="force"/>; calls while a check runs wait for it.
    /// </summary>
    /// <param name="force">True to check again.</param>
    /// <returns>A task that completes when every row has its state.</returns>
    public async Task RefreshAsync(bool force)
    {
        await LoadAsync();
        if (_refresh is { IsCompleted: false } running)
        {
            await running;
            return;
        }

        var context = _context();
        UpdateDiscovery(context.Url);
        if (!force && _probed && string.Equals(_probedFor, context.WorkingDirectory, StringComparison.Ordinal))
        {
            return;
        }

        _refresh = RefreshCoreAsync(context);
        await _refresh;
    }

    /// <summary>
    /// Registers a client without the row's confirmations (the first-start dialog shows the exact text first), updates
    /// its row and remembers it as the last registered client.
    /// </summary>
    /// <param name="item">The row.</param>
    /// <param name="scope">The scope, or null for the row's.</param>
    /// <returns>The outcome.</returns>
    public async Task<AgentClientRegisterResult> RegisterProfileAsync(AgentClientItemViewModel item, string? scope)
    {
        ArgumentNullException.ThrowIfNull(item);
        var context = item.Profile.UsesHttp ? await WithTokenAsync("Register with " + item.Name) : _context();
        return await RunAsync(item, () => Registry.RegisterAsync(item.Profile, context, scope ?? item.SelectedScope, CancellationToken.None), registered: true);
    }

    /// <summary>Asks whether the token may go into a copied text or a registration.</summary>
    /// <param name="dialogs">The dialogs.</param>
    /// <param name="title">The dialog title.</param>
    /// <returns>True when the user allowed it.</returns>
    public static Task<bool> ConfirmTokenAsync(IDialogService dialogs, string title)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        return dialogs.ConfirmAsync(
            title,
            "Put the agent token into the text? Anything that can read it can then edit in Cutlass until you regenerate the token. Cancel uses the path of the token file instead.",
            "Include token");
    }

    /// <summary>Register or Update on a row: a foreign entry is replaced only after a confirmation.</summary>
    /// <param name="item">The row.</param>
    /// <returns>A task that completes when the client was registered or the user cancelled.</returns>
    internal async Task RegisterAsync(AgentClientItemViewModel item)
    {
        if (item.Probe.State == AgentClientState.Foreign &&
            !await _dialogs.ConfirmAsync(
                "Replace the cutlass entry",
                item.Name + " already has a cutlass entry that Cutlass did not write (" + item.ToolTip + "). Replace it with this Cutlass?" +
                (item.Profile.Kind == AgentClientKind.ConfigFile ? " The old file is kept as .bak." : string.Empty),
                "Replace"))
        {
            return;
        }

        await RegisterProfileAsync(item, null);
    }

    /// <summary>Remove on a row, after a confirmation.</summary>
    /// <param name="item">The row.</param>
    /// <returns>A task that completes when the entry was removed or the user cancelled.</returns>
    internal async Task RemoveAsync(AgentClientItemViewModel item)
    {
        if (!await _dialogs.ConfirmAsync(
                "Remove the cutlass entry",
                "Remove the cutlass entry from " + item.Name + "? " + item.Name + " no longer starts the Cutlass MCP server afterwards." +
                (item.Profile.Kind == AgentClientKind.ConfigFile ? " The old file is kept as .bak." : string.Empty),
                "Remove"))
        {
            return;
        }

        var context = _context();
        await RunAsync(item, () => Registry.RemoveAsync(item.Profile, context, CancellationToken.None), registered: false);
    }

    /// <summary>Copy on a row: the setup text, with the token only after a confirmation for HTTP clients.</summary>
    /// <param name="item">The row.</param>
    /// <returns>A task that completes when the text is on the clipboard.</returns>
    internal async Task CopyAsync(AgentClientItemViewModel item)
    {
        var context = item.Profile.UsesHttp ? await WithTokenAsync("Copy the setup for " + item.Name) : _context();
        var text = Registry.RegistrationText(item.Profile, context, item.SelectedScope);
        await _dialogs.CopyToClipboardAsync(text);
        OutputText = "Copied the setup for " + item.Name + ":\n" + text;
    }

    private async Task<AgentClientContext> WithTokenAsync(string title)
    {
        var context = _context();
        if (!await ConfirmTokenAsync(_dialogs, title))
        {
            return context;
        }

        return await _readToken() is { Length: > 0 } token ? context with { Token = token } : context;
    }

    private async Task<AgentClientRegisterResult> RunAsync(AgentClientItemViewModel item, Func<Task<AgentClientRegisterResult>> work, bool registered)
    {
        item.IsBusy = true;
        SetBusy(true);
        OutputText = (registered ? "Registering with " : "Removing from ") + item.Name + "...";
        AgentClientRegisterResult result;
        try
        {
            result = await work();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            result = new AgentClientRegisterResult(false, ex.Message, new AgentClientProbe(item.Probe.Installed, item.Probe.ConfigPath, AgentClientState.Error, ex.Message));
        }
        finally
        {
            item.IsBusy = false;
            SetBusy(false);
        }

        item.Probe = result.Probe;
        var hint = !result.Succeeded
            ? (registered ? "The registration failed; see above." : "The removal failed; see above.")
            : registered
                ? "Registered with " + item.Name + "." + (item.Profile.Note is { Length: > 0 } note ? " " + note : string.Empty)
                : "Removed from " + item.Name + ".";
        OutputText = result.Output + "\n\n" + hint;
        UpdateSummary();
        if (registered && result.Succeeded)
        {
            try
            {
                await Registry.SaveLastRegisteredAsync(item.Id, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Only the plain briefing's default target is lost.
                OutputText += "\n(The last registered client could not be remembered: " + ex.Message + ")";
            }

            Registered?.Invoke(this, item.Id);
        }

        return result;
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        foreach (var row in Items)
        {
            row.NotifyBusyChanged();
        }
    }

    private async Task LoadCoreAsync()
    {
        var profiles = await Registry.LoadAsync(CancellationToken.None);
        var context = _context();
        var rows = new List<AgentClientItemViewModel>();
        foreach (var profile in profiles)
        {
            var where = profile.Kind switch
            {
                AgentClientKind.Cli => "via the " + profile.Register!.Command + " command line tool",
                AgentClientKind.ConfigFile => Registry.ConfigPath(profile) ?? "its configuration file",
                _ => "copy the setup text (" + profile.TransportText + ")",
            };
            rows.Add(new AgentClientItemViewModel(this, profile, where));
        }

        Items.Clear();
        foreach (var row in rows.Where(r => !r.IsTextOnly).Concat(rows.Where(r => r.IsTextOnly)))
        {
            Items.Add(row);
        }

        UpdateDiscovery(context.Url);
    }

    private async Task RefreshCoreAsync(AgentClientContext context)
    {
        IsRefreshing = true;
        SummaryText = "checking...";
        var rows = Items.Where(i => !i.IsTextOnly).ToList();
        foreach (var row in rows)
        {
            row.Probe = AgentClientProbe.Checking;
        }

        var probes = await Task.WhenAll(rows.Select(row => ProbeAsync(row.Profile, context)));
        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Probe = probes[i];
        }

        _probed = true;
        _probedFor = context.WorkingDirectory;
        Sort();
        IsRefreshing = false;
        UpdateSummary();
    }

    private async Task<AgentClientProbe> ProbeAsync(AgentClientProfile profile, AgentClientContext context)
    {
        try
        {
            return await Registry.ProbeAsync(profile, context, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new AgentClientProbe(false, null, AgentClientState.Error, ex.Message);
        }
    }

    // Detected clients first (in profile order), then the others, then the text-only rows.
    private void Sort()
    {
        var ordered = Items.Where(i => !i.IsTextOnly && i.Probe.Installed)
            .Concat(Items.Where(i => !i.IsTextOnly && !i.Probe.Installed))
            .Concat(Items.Where(i => i.IsTextOnly))
            .ToList();
        for (var target = 0; target < ordered.Count; target++)
        {
            var from = Items.IndexOf(ordered[target]);
            if (from != target)
            {
                Items.Move(from, target);
            }
        }
    }

    private void UpdateSummary()
    {
        CurrentCount = Items.Count(i => i.IsCurrent);
        SummaryText = !_probed ? "not checked"
            : CurrentCount == 0 ? "none current"
            : CurrentCount.ToString(CultureInfo.InvariantCulture) + " current";
        var detected = Items.Where(i => !i.IsTextOnly && i.Probe.Installed).Select(i => i.Name + ": " + i.StateText).ToList();
        SummaryToolTip = detected.Count == 0
            ? "No agent client was detected on this machine. Connect... lists every client Cutlass knows."
            : string.Join("\n", detected);
    }

    private void UpdateDiscovery(string url)
    {
        if (DiscoveryUrls.Count > 0 && DiscoveryUrls[0].Url.StartsWith(url, StringComparison.Ordinal))
        {
            return;
        }

        DiscoveryUrls.Clear();
        foreach (var format in Registry.ToolFormats)
        {
            DiscoveryUrls.Add(new AgentDiscoveryUrlItem("Tools (" + format + ")", url + "v1/tools?format=" + format, CopyUrlAsync));
        }

        DiscoveryUrls.Add(new AgentDiscoveryUrlItem("OpenAPI document", url + "v1/openapi.json", CopyUrlAsync));
        DiscoveryUrls.Add(new AgentDiscoveryUrlItem("Instructions", url + "v1/instructions", CopyUrlAsync));
    }

    private async Task CopyUrlAsync(string url)
    {
        await _dialogs.CopyToClipboardAsync(url);
        OutputText = "Copied " + url;
    }

    /// <summary>Checks every client again.</summary>
    /// <returns>A task that completes when every row has its state.</returns>
    [RelayCommand]
    private Task RefreshNowAsync() => RefreshAsync(force: true);

    /// <summary>Closes the dialog.</summary>
    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);
}
