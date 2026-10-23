using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cutlass.App.Services.Agent.Clients.Profiles;

namespace Cutlass.App.Services.Agent.Clients;

/// <summary>Probe, register, update and remove (AGENT_INTEGRATION 9.3) for the three profile kinds.</summary>
public sealed partial class AgentClientRegistry
{
    /// <summary>
    /// Checks one client: whether it is installed and what its <c>cutlass</c> entry points at. A CLI profile runs its
    /// check command (and the fallback when the check fails unexpectedly); a configuration file profile reads the
    /// file. Text rows report <see cref="AgentClientState.Unknown"/>. Off the calling thread.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="context">The live values (the exe to compare with, the CLI's folder).</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The probe.</returns>
    public async Task<AgentClientProbe> ProbeAsync(AgentClientProfile profile, AgentClientContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        switch (profile.Kind)
        {
            case AgentClientKind.Cli:
                return await ProbeCliAsync(profile, context, cancellationToken).ConfigureAwait(false);
            case AgentClientKind.ConfigFile:
                return await Task.Run(() => ProbeConfig(profile, context), cancellationToken).ConfigureAwait(false);
            default:
                return new AgentClientProbe(true, null, AgentClientState.Unknown);
        }
    }

    /// <summary>
    /// Registers (or updates) the Cutlass MCP server with a client. A CLI profile removes an existing <c>cutlass</c>
    /// entry from its scope first, then runs its register command; a configuration file profile merges the entry
    /// (only that entry changes, the old file is kept as <c>.bak</c>). The state is checked again afterwards. A
    /// foreign entry is replaced too: the caller asks the user first.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="context">The live values.</param>
    /// <param name="scope">The scope for a CLI profile, or null for its default.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    public async Task<AgentClientRegisterResult> RegisterAsync(AgentClientProfile profile, AgentClientContext context, string? scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        return profile.Kind switch
        {
            AgentClientKind.Cli => await RegisterCliAsync(profile, context, scope, cancellationToken).ConfigureAwait(false),
            AgentClientKind.ConfigFile => await EditConfigAsync(profile, context, remove: false, cancellationToken).ConfigureAwait(false),
            _ => new AgentClientRegisterResult(false, profile.Name + " is set up by hand: copy its text instead.", new AgentClientProbe(true, null, AgentClientState.Unknown)),
        };
    }

    /// <summary>Removes the <c>cutlass</c> entry from a client (its CLI, or its configuration file with a <c>.bak</c>).</summary>
    /// <param name="profile">The profile.</param>
    /// <param name="context">The live values.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome.</returns>
    public async Task<AgentClientRegisterResult> RemoveAsync(AgentClientProfile profile, AgentClientContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        return profile.Kind switch
        {
            AgentClientKind.Cli => await RemoveCliAsync(profile, context, cancellationToken).ConfigureAwait(false),
            AgentClientKind.ConfigFile => await EditConfigAsync(profile, context, remove: true, cancellationToken).ConfigureAwait(false),
            _ => new AgentClientRegisterResult(false, profile.Name + " has nothing registered.", new AgentClientProbe(true, null, AgentClientState.Unknown)),
        };
    }

    private async Task<AgentClientProbe> ProbeCliAsync(AgentClientProfile profile, AgentClientContext context, CancellationToken cancellationToken)
    {
        if (!await DetectAsync(profile).ConfigureAwait(false))
        {
            return new AgentClientProbe(false, null, AgentClientState.NotInstalled, (profile.Detect?.Command ?? profile.Register!.Command) + " was not found on PATH.");
        }

        if (profile.Probe is not { } spec)
        {
            return new AgentClientProbe(true, null, AgentClientState.Unknown);
        }

        var values = Values(context, null);
        var expected = Expand(spec.RegisteredWhen ?? "{exe}", values);
        var result = await _runner.RunAsync(spec.Command, ExpandArgs(spec.Args, values), context.WorkingDirectory, CallTimeout, cancellationToken).ConfigureAwait(false);
        var probe = AgentClientCliParser.Parse(result, spec, expected, CallTimeout);
        if (probe.State == AgentClientState.Error && !result.TimedOut && spec.FallbackArgs is { Count: > 0 } fallbackArgs)
        {
            var list = await _runner.RunAsync(spec.Command, ExpandArgs(fallbackArgs, values), context.WorkingDirectory, CallTimeout, cancellationToken).ConfigureAwait(false);
            var fromList = AgentClientCliParser.Parse(list, spec, expected, CallTimeout);
            if (fromList.State != AgentClientState.Error)
            {
                probe = fromList;
            }
        }

        return probe;
    }

    private async Task<AgentClientRegisterResult> RegisterCliAsync(AgentClientProfile profile, AgentClientContext context, string? scope, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var before = await ProbeCliAsync(profile, context, cancellationToken).ConfigureAwait(false);
        if (before.State == AgentClientState.NotInstalled)
        {
            return new AgentClientRegisterResult(false, profile.Register!.Command + " was not found on PATH. Install " + profile.Name + " first.", before);
        }

        if (before.Exists && profile.Remove is { } remove)
        {
            await RunAsync(remove, Values(context, before.Scope), context, output, cancellationToken).ConfigureAwait(false);
        }

        var added = await RunAsync(profile.Register!, Values(context, string.IsNullOrEmpty(scope) ? profile.DefaultScope : scope), context, output, cancellationToken).ConfigureAwait(false);
        var after = await ProbeCliAsync(profile, context, cancellationToken).ConfigureAwait(false);
        return new AgentClientRegisterResult(added.Succeeded, output.ToString().TrimEnd(), after);
    }

    private async Task<AgentClientRegisterResult> RemoveCliAsync(AgentClientProfile profile, AgentClientContext context, CancellationToken cancellationToken)
    {
        var before = await ProbeCliAsync(profile, context, cancellationToken).ConfigureAwait(false);
        if (!before.Exists)
        {
            return new AgentClientRegisterResult(before.State == AgentClientState.NotRegistered, profile.Name + " has no cutlass entry; nothing was removed.", before);
        }

        if (profile.Remove is not { } remove)
        {
            return new AgentClientRegisterResult(false, profile.Name + " has no remove command in its profile.", before);
        }

        var output = new StringBuilder();
        var removed = await RunAsync(remove, Values(context, before.Scope), context, output, cancellationToken).ConfigureAwait(false);
        var after = await ProbeCliAsync(profile, context, cancellationToken).ConfigureAwait(false);
        return new AgentClientRegisterResult(removed.Succeeded, output.ToString().TrimEnd(), after);
    }

    private async Task<CliResult> RunAsync(AgentClientCommand command, IReadOnlyDictionary<string, string> values, AgentClientContext context, StringBuilder output, CancellationToken cancellationToken)
    {
        var args = ExpandArgs(command.Args, values);
        output.Append("> ").Append(CommandLine(command.Command, args)).Append('\n');
        var result = await _runner.RunAsync(command.Command, args, context.WorkingDirectory, CallTimeout, cancellationToken).ConfigureAwait(false);
        if (result.CombinedOutput is { Length: > 0 } text)
        {
            output.Append(text).Append('\n');
        }

        if (result.TimedOut)
        {
            output.Append("(timed out)\n");
        }
        else if (!result.Succeeded)
        {
            output.Append("(exit code ").Append(result.ExitCode.ToString(CultureInfo.InvariantCulture)).Append(")\n");
        }

        return result;
    }

    private AgentClientProbe ProbeConfig(AgentClientProfile profile, AgentClientContext context)
    {
        var installed = Detect(profile);
        if (ConfigPath(profile) is not { } path)
        {
            return new AgentClientProbe(installed, null, installed ? AgentClientState.Error : AgentClientState.NotInstalled, profile.Name + " has no configuration file on this platform.");
        }

        if (!File.Exists(path))
        {
            return new AgentClientProbe(installed, path, installed ? AgentClientState.NotRegistered : AgentClientState.NotInstalled);
        }

        JsonNode? actual;
        try
        {
            var (text, _) = ReadText(path);
            actual = profile.IsToml ? TomlTableEditor.Read(text, profile.ConfigEntry!.Segments) : JsoncEditor.Read(text, profile.ConfigEntry!.Segments);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException)
        {
            return new AgentClientProbe(true, path, AgentClientState.Error, "The configuration file cannot be read: " + ex.Message);
        }

        if (actual is null)
        {
            return new AgentClientProbe(true, path, AgentClientState.NotRegistered);
        }

        var expected = ExpandedEntry(profile, context);
        var detail = Describe(actual);
        if (Matches(expected, actual))
        {
            return new AgentClientProbe(true, path, AgentClientState.Current, detail);
        }

        return new AgentClientProbe(true, path, Mentions(actual, AgentClientCliParser.McpExeName) ? AgentClientState.Stale : AgentClientState.Foreign, detail);
    }

    private async Task<AgentClientRegisterResult> EditConfigAsync(AgentClientProfile profile, AgentClientContext context, bool remove, CancellationToken cancellationToken)
    {
        var message = await Task.Run(() => EditConfig(profile, context, remove), cancellationToken).ConfigureAwait(false);
        var after = await Task.Run(() => ProbeConfig(profile, context), cancellationToken).ConfigureAwait(false);
        return new AgentClientRegisterResult(message.Succeeded, message.Text, after);
    }

    private (bool Succeeded, string Text) EditConfig(AgentClientProfile profile, AgentClientContext context, bool remove)
    {
        if (ConfigPath(profile) is not { } path)
        {
            return (false, profile.Name + " has no configuration file on this platform.");
        }

        var segments = profile.ConfigEntry!.Segments;
        try
        {
            var exists = File.Exists(path);
            var (text, bom) = exists ? ReadText(path) : (string.Empty, false);
            string updated;
            if (remove)
            {
                bool removed;
                updated = profile.IsToml ? TomlTableEditor.Remove(text, segments, out removed) : JsoncEditor.Remove(text, segments, out removed);
                if (!removed)
                {
                    return (true, path + " has no " + profile.ConfigEntry.Path + " entry; nothing was changed.");
                }
            }
            else
            {
                var entry = ExpandedEntry(profile, context);
                updated = profile.IsToml
                    ? TomlTableEditor.Upsert(text, segments, entry as JsonObject ?? new JsonObject())
                    : JsoncEditor.Upsert(text, segments, entry);
                if (string.Equals(updated, text, StringComparison.Ordinal))
                {
                    return (true, path + " already holds this entry; nothing was changed.");
                }
            }

            WriteAtomic(path, updated, bom, backup: exists);
            var verb = remove ? "Removed " + profile.ConfigEntry.Path + " from " : "Wrote " + profile.ConfigEntry.Path + " to ";
            return (true, verb + path + (exists ? " (the previous file is " + Path.GetFileName(path) + ".bak)." : "."));
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return (false, path + " is not valid " + (profile.IsToml ? "TOML" : "JSON") + " (" + ex.Message + "); nothing was changed. Fix it or add the entry by hand (Copy).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, path + " could not be written: " + ex.Message);
        }
    }

    private static (string Text, bool Bom) ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return (Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)), bom);
    }

    // Every member the profile writes must be there with the same value; members the client added stay allowed. A
    // missing empty array counts as empty, and strings that are paths compare as paths.
    private static bool Matches(JsonNode? expected, JsonNode? actual)
    {
        switch (expected)
        {
            case JsonObject e:
                if (actual is not JsonObject a)
                {
                    return false;
                }

                foreach (var (key, value) in e)
                {
                    if (!a.TryGetPropertyValue(key, out var other))
                    {
                        if (value is JsonArray { Count: 0 })
                        {
                            continue;
                        }

                        return false;
                    }

                    if (!Matches(value, other))
                    {
                        return false;
                    }
                }

                return true;
            case JsonArray e:
                return actual is JsonArray b && b.Count == e.Count && e.Zip(b).All(p => Matches(p.First, p.Second));
            case JsonValue e when e.TryGetValue<string>(out var s):
                return actual is JsonValue v && v.TryGetValue<string>(out var t) &&
                    (string.Equals(s, t, StringComparison.Ordinal) || (LooksLikePath(s) && AgentClientCliParser.SamePath(s, t)));
            default:
                return JsonNode.DeepEquals(expected, actual);
        }
    }

    private static bool LooksLikePath(string text) =>
        text.Contains('/', StringComparison.Ordinal) || text.Contains('\\', StringComparison.Ordinal);

    private static bool Mentions(JsonNode? node, string text) => node switch
    {
        JsonObject obj => obj.Any(p => Mentions(p.Value, text)),
        JsonArray array => array.Any(e => Mentions(e, text)),
        JsonValue v when v.TryGetValue<string>(out var s) => s.Contains(text, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    // "command arg arg" for a server entry, else the compact JSON.
    private static string Describe(JsonNode node)
    {
        if (node is JsonObject obj && obj["command"] is JsonValue c && c.TryGetValue<string>(out var command))
        {
            var args = obj["args"] is JsonArray a ? a.Select(e => e is JsonValue v && v.TryGetValue<string>(out var s) ? s : e?.ToJsonString() ?? string.Empty).ToList() : [];
            return CommandLine(command, args);
        }

        return node.ToJsonString();
    }
}
