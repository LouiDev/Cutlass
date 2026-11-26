using System.Globalization;
using Cutlass.App.Services.Commands;
using Cutlass.App.ViewModels.Agent;
using Cutlass.App.ViewModels.Dialogs;
using Cutlass.Media.Projects;

namespace Cutlass.App.Services.Smoke;

/// <summary>
/// The M7 dialog and relink commands: <c>show-dialog</c> opens a dialog through the command the menus and keys use,
/// takes the view model the smoke dialog service was asked to show and renders it in its real window through
/// <see cref="CaptureDialog"/> (so the dialogs can be checked against DESIGN 8 in screenshots); <c>remove-file</c>
/// takes media offline and <c>expect-offline</c> checks the bin's link state (DESIGN 11).
/// </summary>
public sealed partial class SmokeRunner
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Opens the window of a dialog view model, renders it into a PNG file and closes it (set by the app; the runner
    /// itself touches no controls); the third argument is a window height for the capture, or null. Without it
    /// <c>show-dialog</c> fails.
    /// </summary>
    public Func<object, string, double?, Task>? CaptureDialog { get; init; }

    // show-dialog settings|relink|export|speed|sequence|mcp-start|connect-clients [page=<page>] [media=<media>] screenshot=<file.png>
    private async Task ShowDialogAsync(SmokeStep step)
    {
        Expect(_dialogs is not null, "show-dialog needs the smoke dialog service");
        Expect(CaptureDialog is not null, "show-dialog needs a dialog capture");
        var kind = Arg(step, 0).ToLowerInvariant();
        var shot = step.Option("screenshot") is { Length: > 0 } file ? ResolvePath(file) : throw Fail("show-dialog needs screenshot=<file.png>");
        var before = _dialogs!.LastDialog;
        switch (kind)
        {
            case "settings":
                RunRegistered(CommandIds.Settings);
                break;
            case "relink":
                if (step.Option("media") is { } reference)
                {
                    // Relink... on a bin item (DESIGN 4.2).
                    var asset = Media(reference);
                    var item = _vm.MediaBin.Items.FirstOrDefault(i => i.Id == asset.Id) ?? throw Fail("no bin item for " + reference);
                    await _vm.MediaBin.RelinkCommand.ExecuteAsync(item);
                }
                else
                {
                    Expect(_vm.MediaBin.OfflineMediaIds.Count > 0, "no media is offline, so File > Relink media... shows nothing");
                    RunRegistered(CommandIds.RelinkMedia);
                }

                break;
            case "export":
                RunRegistered(CommandIds.Export);
                break;
            case "speed":
                RunRegistered(CommandIds.Speed);
                break;
            case "sequence":
                RunRegistered(CommandIds.SequenceSettings);
                break;
            case "mcp-start":
                // The first-start dialog of Agent > Start MCP server shows only once, so it is created directly here,
                // from the client list's check (AGENT_INTEGRATION 9.4: the detected clients in the dropdown).
                if (_vm.Clients is { } startClients)
                {
                    await startClients.RefreshAsync(force: false);
                }

                var mcp = _vm.CreateMcpStartDialog();
                await CaptureDialog!(mcp, shot, null);
                _log.Write("screenshot of the " + mcp.GetType().Name + " window (" + string.Join(", ", mcp.Choices.Select(c => c.DisplayText)) + "): " + shot);
                return;
            case "connect-clients":
                RunRegistered(CommandIds.AgentConnectClients);
                break;
            default:
                throw Fail("show-dialog knows settings, relink, export, speed, sequence, mcp-start and connect-clients, not " + kind);
        }

        var deadline = DateTime.UtcNow + DialogTimeout;
        while (ReferenceEquals(_dialogs.LastDialog, before) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        var dialog = _dialogs.LastDialog is { } shown && !ReferenceEquals(shown, before)
            ? shown
            : throw Fail("the " + kind + " dialog did not open");
        if (dialog is SettingsViewModel settings)
        {
            if (step.Option("page") is { } page)
            {
                settings.SelectedPageIndex = (int)(Enum.TryParse<SettingsPage>(page, ignoreCase: true, out var p) ? p : throw Fail("no settings page " + page));
            }

            await settings.LoadAsync();
            if (settings.Clients is { } settingsClients)
            {
                await settingsClients.RefreshAsync(force: false);
            }
        }
        else if (dialog is ConnectClientsViewModel clients)
        {
            // The menu command started a check; the screenshot shows its result.
            await clients.RefreshAsync(force: false);
        }

        // height=<px> renders a taller window, so a long page (Settings > Agents) shows its lower groups.
        double? height = step.Option("height") is { } h ? double.Parse(h, NumberStyles.Float, CultureInfo.InvariantCulture) : null;
        await CaptureDialog!(dialog, shot, height);
        _log.Write("screenshot of the " + dialog.GetType().Name + " window: " + shot);
    }

    private void RunRegistered(string id)
    {
        Expect(_vm.Commands.CanExecute(id), "the command " + id + " cannot run now");
        Expect(_vm.Commands.TryExecute(id), "the command " + id + " is not registered");
    }

    // remove-file <path>: a scrub or job process may still read the file for a moment (Windows refuses to delete an
    // open file), so the delete is retried for a few seconds.
    private async Task RemoveFileAsync(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                await Task.Run(() => File.Delete(path));
                Expect(!File.Exists(path), "the file " + path + " still exists");
                _log.Write("removed " + path);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
            }
        }
    }

    // expect-offline <media> [online]
    private void ExpectOffline(SmokeStep step)
    {
        var asset = Media(Arg(step, 0));
        var state = _vm.MediaBin.LinkStateOf(asset.Id);
        var wantOnline = step.Flag("online");
        Expect(
            wantOnline ? state != MediaLinkState.Offline : state == MediaLinkState.Offline,
            string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(asset.Path)} is {state}, expected {(wantOnline ? "online" : "offline")}"));
        Expect(
            _vm.Timeline.IsMediaOffline?.Invoke(asset.Id) == !wantOnline,
            "the timeline does not see the link state of " + Path.GetFileName(asset.Path));
        _log.Write(Path.GetFileName(asset.Path) + " is " + state);
    }
}
