using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cutlass.App.Services.Agent;
using Cutlass.App.Services.Commands;
using Cutlass.App.ViewModels;

namespace Cutlass.App.Services.Smoke;

/// <summary>
/// The agent smoke commands (AGENT_INTEGRATION 6.4 and 7.3). They drive the App's own agent server over real HTTP
/// through an in-process agent client: <c>agent-connect</c> reads <c>endpoint.json</c> and the token file,
/// <c>agent-call</c> posts <c>/v1/call</c> (with the lock header once <c>lock.acquire</c> granted one), and the
/// expectations check both the banner's view model and <c>/v1/lock</c>. JSON arguments write strings in single
/// quotes (the script tokenizer removes double quotes); <c>$name</c> takes a value saved earlier with
/// <c>save=name=path</c>, and <c>$dir</c> is the script's folder with forward slashes.
/// </summary>
public sealed partial class SmokeRunner
{
    private static readonly TimeSpan AgentLogWait = TimeSpan.FromSeconds(3);
    private readonly Dictionary<string, string> _agentVars = new(StringComparer.Ordinal);
    private SmokeAgentConnection? _agent;

    /// <summary>The App's agent server (the address and token for <c>agent-connect</c>), or null.</summary>
    public AgentServerController? AgentServer { get; init; }

    /// <summary>
    /// Turns a script JSON argument into JSON: single quotes become double quotes and <c>$name</c> becomes the saved
    /// value (inserted as is, so a saved string sits inside quotes of the script and a saved number outside them).
    /// </summary>
    /// <param name="text">The argument, for example <c>{'mediaId':'$m1','at':0}</c>.</param>
    /// <param name="vars">The saved values.</param>
    /// <returns>The JSON text.</returns>
    /// <exception cref="FormatException">When a <c>$name</c> was never saved or the result is not JSON.</exception>
    public static string ToJson(string text, IReadOnlyDictionary<string, string> vars)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(vars);
        var replaced = VariablePattern().Replace(text, m =>
            vars.TryGetValue(m.Groups[1].Value, out var value)
                ? value
                : throw new FormatException("$" + m.Groups[1].Value + " was not saved by an earlier agent-call."));
        var json = replaced.Replace('\'', '"');
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Not JSON: " + json + " (" + ex.Message + ")", ex);
        }

        return json;
    }

    /// <summary>
    /// Reads a value from a JSON document by a path: dot-separated property names, <c>[n]</c> indexes and
    /// <c>[field=value]</c> filters on arrays, for example <c>result.tracks[name=V2].id</c>.
    /// </summary>
    /// <param name="root">The document root.</param>
    /// <param name="path">The path.</param>
    /// <returns>The value as text (strings unquoted, other values as raw JSON), or null when the path does not resolve.</returns>
    public static string? JsonPath(JsonElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var current = root;
        foreach (Match part in PathPattern().Matches(path))
        {
            if (part.Groups["name"].Success)
            {
                if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part.Groups["name"].Value, out current))
                {
                    return null;
                }
            }
            else if (part.Groups["index"].Success)
            {
                var index = int.Parse(part.Groups["index"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
                if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
            }
            else
            {
                var field = part.Groups["field"].Value;
                var value = part.Groups["value"].Value;
                if (current.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var found = current.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(field, out var f) && Text(f) == value).ToList();
                if (found.Count == 0)
                {
                    return null;
                }

                current = found[0];
            }
        }

        return Text(current);

        static string Text(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText();
    }

    [GeneratedRegex(@"\$([A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex VariablePattern();

    [GeneratedRegex(@"(?<name>[A-Za-z_][A-Za-z0-9_]*)|\[(?<index>[0-9]+)\]|\[(?<field>[A-Za-z_][A-Za-z0-9_]*)=(?<value>[^\]]*)\]")]
    private static partial Regex PathPattern();

    private async Task<bool> ExecuteAgentAsync(SmokeStep step)
    {
        switch (step.Command)
        {
            case "agent-connect":
                await AgentConnectAsync(step);
                return true;
            case "agent-call":
                await AgentCallAsync(step);
                return true;
            case "expect-locked":
                await ExpectLockedAsync(step);
                return true;
            case "expect-unlocked":
                await ExpectUnlockedAsync();
                return true;
            case "agent-takeover":
                AgentButton(_vm.Agent.TakeOverCommand, "Take over");
                Expect(!_vm.Agent.IsLocked, "the lock is still held after Take over");
                Expect(!_vm.Timeline.IsReadOnly, "the timeline is still read-only after Take over");
                return true;
            case "agent-pause":
                Expect(_vm.Agent.IsHeld, "no agent holds the lease, so there is nothing to pause");
                AgentButton(_vm.Agent.PauseResumeCommand, "Pause");
                Expect(_vm.Agent.IsPaused, "the lock did not pause");
                return true;
            case "agent-resume":
                Expect(_vm.Agent.IsPaused, "the lock is not paused");
                AgentButton(_vm.Agent.PauseResumeCommand, "Resume");
                Expect(_vm.Agent.IsHeld, "the lock did not resume");
                return true;
            case "expect-readonly":
                await ExpectReadOnlyAsync(step);
                return true;
            case "expect-agent-log":
                await ExpectAgentLogAsync(step);
                return true;
            case "expect-no-refused":
                Expect(_vm.AgentWriteRefusedCount == 0, string.Create(CultureInfo.InvariantCulture, $"the session write gate refused {_vm.AgentWriteRefusedCount} user write(s); the UI should have blocked them up front"));
                _log.Write("the write gate never refused a user write");
                return true;
            case "expect-duration-below":
                var limit = long.Parse(Arg(step, 0), NumberStyles.None, CultureInfo.InvariantCulture);
                Expect(_vm.Timeline.SequenceEnd < limit, string.Create(CultureInfo.InvariantCulture, $"the sequence has {_vm.Timeline.SequenceEnd} frames, expected fewer than {limit}"));
                _log.Write(string.Create(CultureInfo.InvariantCulture, $"the sequence has {_vm.Timeline.SequenceEnd} frames"));
                return true;
            default:
                return false;
        }
    }

    private SmokeAgentConnection Agent => _agent ?? throw Fail("agent-connect first");

    private void AgentButton(System.Windows.Input.ICommand command, string what)
    {
        Expect(command.CanExecute(null), what + " is disabled");
        command.Execute(null);
        _log.Write(what + ": " + _vm.Agent.PhaseText);
    }

    private async Task AgentConnectAsync(SmokeStep step)
    {
        var name = step.Option("name") ?? "Smoke";
        _agentVars["dir"] = _baseDirectory.Replace('\\', '/');
        _agent?.Dispose();
        _agent = await SmokeAgentConnection.ConnectAsync(_services.Paths.AgentDirectory, name, CancellationToken.None);
        _log.Write("agent-connect " + _agent.BaseUrl + " as " + name);
        var status = await _agent.CallAsync("status", "{}", CancellationToken.None);
        Expect(status.Code == "ok" && status.Json is not null, "status answered " + status.Code + ": " + status.Message);
        _log.Write("status: " + Shorten(status.Json!));
    }

    private async Task AgentCallAsync(SmokeStep step)
    {
        var positional = step.Positional();
        Expect(positional.Count >= 1, "missing argument 1: " + SmokeScript.Commands[step.Command]);
        var tool = positional[0];
        var args = ToJson(positional.Count > 1 ? positional[1] : "{}", _agentVars);
        var expected = step.Option("expect") ?? "ok";
        var until = step.Option("until");
        var deadline = DateTime.UtcNow + Seconds(step, "timeout", TimeSpan.FromSeconds(60));
        SmokeAgentResponse response;
        var watch = Stopwatch.StartNew();
        while (true)
        {
            response = await Agent.CallAsync(tool, args, CancellationToken.None);
            if (until is null || response.Code != "ok")
            {
                break;
            }

            var split = until.LastIndexOf('=');
            Expect(split > 0, "until takes <path>=<value>");
            var actual = response.Json is { } doc ? JsonPath(doc.RootElement, until[..split]) : null;
            if (actual == until[(split + 1)..])
            {
                break;
            }

            Expect(DateTime.UtcNow < deadline, "until " + until + " was not reached; the last value was " + (actual ?? "missing"));
            _log.Write("  waiting: " + until[..split] + " is " + (actual ?? "missing"));
        }

        _log.Write(string.Create(CultureInfo.InvariantCulture, $"agent-call {tool} -> {response.Code} ({watch.ElapsedMilliseconds} ms){(response.Png is null ? string.Empty : $", PNG {response.Png.Length} bytes")}"));
        if (response.Json is { } json)
        {
            _log.Write("  " + Shorten(json));
        }

        Expect(
            string.Equals(response.Code, expected, StringComparison.Ordinal),
            "agent-call " + tool + " answered " + response.Code + (response.Message is { } m ? ": " + m : string.Empty) + ", expected " + expected);

        foreach (var save in step.Args.Where(a => a.StartsWith("save=", StringComparison.OrdinalIgnoreCase)).Select(a => a[5..]))
        {
            var eq = save.IndexOf('=', StringComparison.Ordinal);
            Expect(eq > 0, "save takes <var>=<path>");
            var value = response.Json is { } doc ? JsonPath(doc.RootElement, save[(eq + 1)..]) : null;
            Expect(value is not null, "the answer has no " + save[(eq + 1)..]);
            _agentVars[save[..eq]] = value!;
            _log.Write("  $" + save[..eq] + " = " + value);
        }

        if (expected == "ok" && tool == "lock.acquire" && response.Json is { } granted && JsonPath(granted.RootElement, "result.lockId") is { } lockId)
        {
            Agent.LockId = lockId;
        }
        else if (expected == "ok" && tool == "lock.release")
        {
            Agent.LockId = null;
        }

        if (step.Option("png") is { } pngPath)
        {
            await SavePngAsync(response, ResolvePath(pngPath));
        }

        // Follow and the banner update on the UI thread; let them run before the next step looks.
        await Task.Delay(30);
    }

    private async Task SavePngAsync(SmokeAgentResponse response, string path)
    {
        var bytes = response.Png;
        if (bytes is null && response.Json is { } json && JsonPath(json.RootElement, "result.pngBase64") is { } base64)
        {
            bytes = Convert.FromBase64String(base64);
        }

        if (bytes is not { Length: > 8 })
        {
            throw Fail("the answer holds no PNG");
        }

        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Expect(bytes.AsSpan(0, 8).SequenceEqual(signature), "the image is not a PNG");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        });
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"saved the PNG ({bytes.Length} bytes) to {path}"));
    }

    private async Task ExpectLockedAsync(SmokeStep step)
    {
        var agent = _vm.Agent;
        var paused = step.Flag("paused");
        var pending = step.Flag("pending");
        if (pending)
        {
            Expect(agent.IsPending, "no agent is waiting for the project (phase " + agent.State.Phase + ")");
        }
        else
        {
            Expect(agent.IsLocked, "no agent holds the project (phase " + agent.State.Phase + ")");
            Expect(agent.IsPaused == paused, paused ? "the lock is not paused" : "the lock is paused");
            Expect(_vm.Timeline.IsReadOnly && _vm.Inspector.IsReadOnly && _vm.MediaBin.IsReadOnly, "the editing UI is not read-only");
        }

        Expect(agent.IsBannerVisible, "the banner does not show");
        if (step.Option("holder") is { } holder)
        {
            var name = pending ? agent.State.PendingAgent : agent.State.AgentName;
            Expect(name == holder, "the holder is " + (name ?? "nobody") + ", expected " + holder);
        }

        var phase = await LockPhaseAsync();
        var expected = pending ? "pending" : paused ? "paused" : "held";
        Expect(phase == expected, "/v1/lock says " + phase + ", expected " + expected);
        _log.Write("banner: " + agent.AgentNameText + " " + agent.HeadlineText + " | " + agent.LastCallText + " | " + agent.ElapsedText + " | " + agent.BadgeText);
    }

    private async Task ExpectUnlockedAsync()
    {
        Expect(!_vm.Agent.IsLocked, "an agent still holds the project");
        Expect(!_vm.Timeline.IsReadOnly && !_vm.Inspector.IsReadOnly && !_vm.MediaBin.IsReadOnly, "the editing UI is still read-only");
        var phase = await LockPhaseAsync();
        Expect(phase is "free" or "pending", "/v1/lock says " + phase + ", expected free");
        _log.Write("unlocked: " + _vm.Agent.HeadlineText);
    }

    private async Task<string> LockPhaseAsync()
    {
        var answer = await Agent.CallAsync("lock.get", "{}", CancellationToken.None);
        Expect(answer.Code == "ok" && answer.Json is not null, "lock.get answered " + answer.Code);
        return JsonPath(answer.Json!.RootElement, "result.phase") ?? "missing";
    }

    private async Task<long> RevisionAsync()
    {
        var status = await Agent.CallAsync("status", "{}", CancellationToken.None);
        Expect(status.Code == "ok" && status.Json is not null, "status answered " + status.Code);
        return long.Parse(JsonPath(status.Json!.RootElement, "revision") ?? "-1", NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    // expect-readonly [keys=Ctrl+Z,Delete]: the gated commands are disabled, the keys are claimed and change nothing.
    private async Task ExpectReadOnlyAsync(SmokeStep step)
    {
        Expect(_vm.Agent.IsLocked, "no agent holds the project");
        foreach (var id in new[] { CommandIds.Lift, CommandIds.Split, CommandIds.Undo, CommandIds.Insert, CommandIds.NewProject, CommandIds.Import, CommandIds.AddMarker })
        {
            Expect(!_vm.Commands.CanExecute(id), id + " can still execute");
        }

        Expect(!_vm.Timeline.UndoCommand.CanExecute(null), "Edit > Undo is enabled");
        Expect(_vm.Commands.CanExecute(CommandIds.ZoomIn), "zoom is gated but must stay allowed");
        var revision = await RevisionAsync();
        var project = _vm.Session.Current;
        var refused = _vm.AgentWriteRefusedCount;
        var keys = (step.Option("keys") ?? "Ctrl+Z,Delete").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var text in keys)
        {
            var chord = KeyChord.TryParse(text, out var c) ? c : throw Fail("not a key chord: " + text);
            var handled = _vm.Keys.HandleKeyDown(chord.Key, chord.Modifiers, textInputFocused: false);
            _vm.Keys.HandleKeyUp(chord.Key);
            Expect(handled, chord + " was not claimed; it would reach the focused control");
            Expect(ReferenceEquals(project, _vm.Session.Current), chord + " changed the project while an agent holds it");
        }

        Expect(_vm.AgentWriteRefusedCount == refused, "a key reached the session (the write gate refused it)");
        Expect(_vm.ToastText == MainWindowViewModel.AgentBlockedMessage, "the toast does not explain the swallowed keys: " + _vm.ToastText);
        var after = await RevisionAsync();
        Expect(after == revision, string.Create(CultureInfo.InvariantCulture, $"the API revision moved from {revision} to {after}"));
        _log.Write(string.Create(CultureInfo.InvariantCulture, $"read-only: {string.Join(", ", keys)} swallowed, revision {after} unchanged"));
    }

    private async Task ExpectAgentLogAsync(SmokeStep step)
    {
        var tool = Arg(step, 0);
        var code = step.Option("code");
        var deadline = DateTime.UtcNow + AgentLogWait;
        while (true)
        {
            if (_vm.Agent.Calls.Any(c => c.Tool == tool && (code is null || c.Code == code)))
            {
                _log.Write("the Agent panel log has " + tool + (code is null ? string.Empty : " " + code));
                return;
            }

            Expect(DateTime.UtcNow < deadline, "the Agent panel log has no " + tool + (code is null ? string.Empty : " with " + code) + "; it has " + string.Join(", ", _vm.Agent.Calls.Take(8).Select(c => c.Tool + " " + c.Code)));
            await Task.Delay(50);
        }
    }

    private static string Shorten(JsonDocument json)
    {
        var text = json.RootElement.GetRawText();
        return text.Length <= 400 ? text : text[..397] + "...";
    }
}
