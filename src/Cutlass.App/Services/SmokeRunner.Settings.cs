using System.Globalization;
using System.Reflection;
using Avalonia.Input;
using Cutlass.App.ViewModels;
using Cutlass.Media.Settings;

namespace Cutlass.App.Services;

/// <summary>
/// The M7 commands: <c>settings</c> drives the Settings dialog's view model (opened through Ctrl+, with <c>key</c>, or
/// created like the menu does), <c>expect-setting</c> checks a value in effect and in <c>settings.json</c>. The
/// settings in effect when the run started are written back when it ends, so a smoke run never changes the user's
/// settings (exports also remember their folder and preset there).
/// </summary>
public sealed partial class SmokeRunner
{
    private AppSettings? _settingsAtStart;

    private void SnapshotSettings() => _settingsAtStart = _services.Settings with { };

    private async Task RestoreSettingsAsync()
    {
        if (_settingsAtStart is not { } start || start == _services.Settings)
        {
            return;
        }

        try
        {
            await _services.ApplySettingsAsync(start, CancellationToken.None);
            _log.Write("restored the settings from the start of the run");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Write("the settings from the start of the run could not be restored: " + ex.Message);
        }
    }

    private async Task<bool> ExecuteSettingsAsync(SmokeStep step)
    {
        switch (step.Command)
        {
            case "settings":
                await SettingsAsync(step);
                return true;
            case "expect-setting":
                await ExpectSettingAsync(step);
                return true;
            case "show-dialog":
                await ShowDialogAsync(step);
                return true;
            case "remove-file":
                await RemoveFileAsync(PathArg(step, 0));
                return true;
            case "expect-offline":
                ExpectOffline(step);
                return true;
            default:
                return false;
        }
    }

    // settings [key] [page=<page>] [<field>=<value>...] [validate] [clear-cache] [apply|cancel]
    private async Task SettingsAsync(SmokeStep step)
    {
        SettingsViewModel dialog;
        if (step.Flag("key"))
        {
            // Ctrl+, through the keymap: the smoke dialog service records the dialog it was asked to show.
            Expect(_dialogs is not null, "settings key needs the smoke dialog service");
            var before = _dialogs!.LastSettingsDialog;
            Expect(_vm.Keys.HandleKeyDown(Key.OemComma, KeyModifiers.Control, textInputFocused: false), "no command took Ctrl+OemComma");
            _vm.Keys.HandleKeyUp(Key.OemComma);
            await Task.Delay(50);
            dialog = _dialogs.LastSettingsDialog is { } shown && !ReferenceEquals(shown, before)
                ? shown
                : throw Fail("Ctrl+, did not open the Settings dialog");
            _log.Write("Ctrl+, opened the Settings dialog on page " + (SettingsPage)dialog.SelectedPageIndex);
        }
        else
        {
            dialog = _vm.CreateSettingsDialog();
        }

        if (step.Option("page") is { } page)
        {
            dialog.SelectedPageIndex = (int)(Enum.TryParse<SettingsPage>(page, ignoreCase: true, out var p) ? p : throw Fail("no settings page " + page));
        }

        await dialog.LoadAsync();
        _log.Write("settings: cache " + dialog.CacheFolder + " (" + dialog.CacheUsageText + "), audio devices: " + string.Join(" | ", dialog.AudioDevices));
        _log.Write("settings: " + dialog.FfmpegStatusText + "; " + dialog.AboutFfmpegText + "; " + SettingsViewModel.VersionText);
        SetSettingsFields(dialog, step);

        // AGENT_INTEGRATION 6.1: agent-read=<folder> and agent-write=<folder> add to the Agents page's folder lists.
        foreach (var arg in step.Args)
        {
            var isRead = arg.StartsWith("agent-read=", StringComparison.OrdinalIgnoreCase);
            if (isRead || arg.StartsWith("agent-write=", StringComparison.OrdinalIgnoreCase))
            {
                var folder = ResolvePath(arg[(arg.IndexOf('=', StringComparison.Ordinal) + 1)..]);
                (isRead ? dialog.AgentReadFolders : dialog.AgentWriteFolders).Add(folder);
                _log.Write("settings: agent may " + (isRead ? "read from " : "write to ") + folder);
            }
        }

        // AGENT_INTEGRATION 9.3: agent-home=<dir> gives the client registry an empty sandbox home inside the script folder.
        if (step.Option("agent-home") is { Length: > 0 } agentHome)
        {
            await UseAgentHomeAsync(agentHome);
        }

        _log.Write("settings problem: " + (dialog.ProblemText ?? "none"));
        if (step.Option("expect-problem") is { } expected)
        {
            Expect(dialog.ProblemText?.Contains(expected, StringComparison.OrdinalIgnoreCase) == true, "the dialog shows no problem with '" + expected + "' (it shows: " + (dialog.ProblemText ?? "none") + ")");
            Expect(!dialog.ApplyCommand.CanExecute(null), "Apply is enabled with an invalid field");
        }

        if (step.Flag("validate"))
        {
            var check = await dialog.CheckFfmpegAsync();
            Expect(check is { IsUsable: true }, "the ffmpeg check failed: " + (check?.StatusText ?? "cancelled"));
            _log.Write("ffmpeg check: " + dialog.FfmpegStatusText);
        }

        if (step.Flag("clear-cache"))
        {
            // The smoke dialog service confirms; the open project's media are kept.
            await dialog.ClearCacheCommand.ExecuteAsync(null);
            _log.Write("cleared the cache: " + dialog.CacheUsageText);
        }

        if (step.Flag("apply"))
        {
            Expect(await dialog.ApplyChangesAsync(), "the settings were not applied: " + (dialog.ApplyErrorText ?? dialog.ProblemText ?? "unknown"));
            _log.Write("applied the settings");
        }
        else if (step.Flag("cancel"))
        {
            dialog.CancelCommand.Execute(null);
            _log.Write("cancelled the settings dialog");
        }
    }

    private static void SetSettingsFields(SettingsViewModel dialog, SmokeStep step)
    {
        foreach (var arg in step.Args)
        {
            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var name = arg[..eq].ToLowerInvariant();
            var value = arg[(eq + 1)..];
            switch (name)
            {
                case "autosave":
                    dialog.AutosaveMinutesText = value;
                    break;
                case "transition":
                    dialog.TransitionSecondsText = value;
                    break;
                case "still":
                    dialog.StillSecondsText = value;
                    break;
                case "title":
                    dialog.TitleSecondsText = value;
                    break;
                case "cache-gb":
                    dialog.CacheLimitText = value;
                    break;
                case "proxy":
                    dialog.SelectedProxyPolicy = Enum.TryParse<ProxyPolicy>(value, ignoreCase: true, out var policy) ? policy : throw Fail("no proxy policy " + value);
                    break;
                case "quality":
                    dialog.SelectedQuality = Enum.TryParse<PreviewQuality>(value, ignoreCase: true, out var quality) ? quality : throw Fail("no preview quality " + value);
                    break;
                case "proxies":
                    dialog.UseProxies = bool.Parse(value);
                    break;
                case "confirm-delete":
                    dialog.ConfirmDeleteTracks = bool.Parse(value);
                    break;
                case "export-preset":
                    dialog.SelectedExportPreset = Compiler.Export.ExportPresets.Find(value) ?? throw Fail("no export preset " + value);
                    break;
                case "ffmpeg":
                    dialog.FfmpegPathText = string.Equals(value, "path-search", StringComparison.OrdinalIgnoreCase) ? string.Empty : value;
                    break;
                case "page":
                case "expect-problem":
                case "agent-read":
                case "agent-write":
                case "agent-home":
                    break;
                default:
                    throw Fail("settings has no field " + name);
            }
        }
    }

    // expect-setting <property> <value>: the AppSettings property (camelCase or PascalCase) in effect and in the file.
    private async Task ExpectSettingAsync(SmokeStep step)
    {
        var name = Arg(step, 0);
        var expected = Arg(step, 1);
        var property = typeof(AppSettings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw Fail("AppSettings has no property " + name);
        var live = Format(property.GetValue(_services.Settings));
        Expect(string.Equals(live, expected, StringComparison.OrdinalIgnoreCase), property.Name + " is " + live + " in effect, expected " + expected);
        var saved = await _services.SettingsStore.LoadAsync();
        var stored = Format(property.GetValue(saved));
        Expect(string.Equals(stored, expected, StringComparison.OrdinalIgnoreCase), property.Name + " is " + stored + " in " + _services.SettingsStore.SettingsFile + ", expected " + expected);
        _log.Write(property.Name + " = " + live + " (in effect and saved)");

        static string Format(object? value) => value switch
        {
            null => "null",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "null",
        };
    }
}
