using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cutlass.App.Services.Agent.Clients.Profiles;

namespace Cutlass.App.Services.Agent.Clients;

/// <summary>
/// The agent clients the app can connect (AGENT_INTEGRATION 9.3). It loads the profiles (the shipped
/// <c>agent-clients.json</c> beside the exe, else the embedded copy, then <c>&lt;data&gt;/agent/clients/*.json</c>
/// merged by id), detects each client in its <see cref="AgentClientEnvironment"/>, probes its registration and
/// registers, updates or removes the Cutlass MCP server: a CLI profile through <see cref="ICliRunner"/> (never
/// <c>ProcessRunner</c>), a configuration file profile by merging its entry into the JSON or TOML file (only that
/// entry changes; the old file is kept as <c>.bak</c> and the new one is written atomically). Placeholders are
/// <c>{exe}</c>, <c>{scope}</c>, <c>{home}</c>, <c>{appdata}</c>, <c>{url}</c> and <c>{token}</c>; the token is only
/// filled in when the context carries a confirmed token. Every file and process call runs off the calling thread.
/// Tests and smoke runs pass a <see cref="AgentClientEnvironment.Sandbox"/> and a fake runner, so they never touch a
/// real client configuration.
/// </summary>
public sealed partial class AgentClientRegistry
{
    /// <summary>The shipped profile file's name beside the exe.</summary>
    public const string ProfileFileName = "agent-clients.json";

    /// <summary>The embedded copy of the shipped profiles (used when the file beside the exe is missing).</summary>
    public const string EmbeddedProfileName = "Cutlass.App.Assets.agent-clients.json";

    /// <summary>How long one client CLI call may take (some CLIs connect to every server to report its health).</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    private readonly string? _shippedFile;
    private readonly string? _userDirectory;
    private readonly string? _stateFile;
    private readonly ICliRunner _runner;
    private Task<AgentClientCatalog>? _load;

    /// <summary>Creates the registry.</summary>
    /// <param name="environment">The home folder, PATH and platform the profiles resolve against.</param>
    /// <param name="runner">Runs client CLIs (a fake one with a sandbox).</param>
    /// <param name="shippedFile">The shipped profile file, or null for the embedded copy.</param>
    /// <param name="userDirectory">The folder of user profiles (<c>&lt;data&gt;/agent/clients</c>), or null.</param>
    /// <param name="stateFile">Where the last registered client is remembered, or null to keep it in memory only.</param>
    /// <exception cref="ArgumentException">A sandbox environment was combined with the real process runner.</exception>
    public AgentClientRegistry(AgentClientEnvironment environment, ICliRunner runner, string? shippedFile, string? userDirectory, string? stateFile)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(runner);
        if (environment.IsSandbox && runner is CliProcess)
        {
            // A fake home must never be combined with the real client CLIs: they would change the real configuration.
            throw new ArgumentException("A sandbox environment needs a fake CLI runner.", nameof(runner));
        }

        Environment = environment;
        _runner = runner;
        _shippedFile = shippedFile;
        _userDirectory = userDirectory;
        _stateFile = stateFile;
    }

    /// <summary>
    /// A registry in a fake home for smoke runs: the shipped profiles from beside the exe, no user profiles, the last
    /// registered client kept in memory only, and the given (fake) runner.
    /// </summary>
    /// <param name="home">The fake home folder.</param>
    /// <param name="runner">A runner that starts no real client CLI.</param>
    /// <returns>The registry.</returns>
    public static AgentClientRegistry ForSandbox(string home, ICliRunner runner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(home);
        return new AgentClientRegistry(AgentClientEnvironment.Sandbox(home), runner, Path.Combine(AppContext.BaseDirectory, ProfileFileName), null, null);
    }

    /// <summary>The folders, PATH and platform the profiles resolve against.</summary>
    public AgentClientEnvironment Environment { get; }

    /// <summary>The loaded catalog (empty until <see cref="LoadAsync"/> completed).</summary>
    public AgentClientCatalog Catalog { get; private set; } = AgentClientCatalog.Empty;

    /// <summary>The loaded profiles in display order (empty until <see cref="LoadAsync"/> completed).</summary>
    public IReadOnlyList<AgentClientProfile> Profiles => Catalog.Clients;

    /// <summary>The <c>/v1/tools?format=</c> values (from the profile file), <c>markdown</c> when none are listed.</summary>
    public IReadOnlyList<string> ToolFormats => Catalog.ToolFormats is { Count: > 0 } formats ? formats : ["markdown"];

    /// <summary>Problems found while loading (an unreadable user profile, a profile without id), for the log.</summary>
    public IReadOnlyList<string> LoadProblems { get; private set; } = [];

    /// <summary>The last registered client id read or written through this registry, or null.</summary>
    public string? LastRegisteredClientId { get; private set; }

    /// <summary>Loads the profiles once, off the calling thread; later calls return the same catalog.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The profiles.</returns>
    public async Task<IReadOnlyList<AgentClientProfile>> LoadAsync(CancellationToken cancellationToken)
    {
        _load ??= Task.Run(LoadCore, CancellationToken.None);
        var catalog = await _load.WaitAsync(cancellationToken).ConfigureAwait(false);
        Catalog = catalog;
        return catalog.Clients;
    }

    /// <summary>Finds a loaded profile by id (case-insensitive).</summary>
    /// <param name="id">The id.</param>
    /// <returns>The profile, or null.</returns>
    public AgentClientProfile? Find(string? id) =>
        id is null ? null : Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses a catalog (the shipped file's format).</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="JsonException">The text is not a profile catalog.</exception>
    public static AgentClientCatalog ParseCatalog(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var catalog = JsonSerializer.Deserialize(json, AgentClientJsonContext.Default.AgentClientCatalog)
            ?? throw new JsonException("The profile file is empty.");
        return catalog with { Clients = catalog.Clients ?? [] };
    }

    /// <summary>Parses a user profile file: one profile, an array of profiles, or a catalog.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The profiles.</returns>
    /// <exception cref="JsonException">The text holds no profile.</exception>
    public static IReadOnlyList<AgentClientProfile> ParseProfiles(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize(json, AgentClientJsonContext.Default.AgentClientProfileArray) ?? [];
        }

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("clients", out _))
        {
            return ParseCatalog(json).Clients;
        }

        var single = JsonSerializer.Deserialize(json, AgentClientJsonContext.Default.AgentClientProfile)
            ?? throw new JsonException("The file holds no profile.");
        return [single];
    }

    /// <summary>
    /// Merges user profiles into the shipped catalog by id: a user profile with a shipped id replaces it in place,
    /// a new id is appended. Profiles without an id or a name are dropped.
    /// </summary>
    /// <param name="shipped">The shipped catalog.</param>
    /// <param name="user">The user profiles in file order.</param>
    /// <returns>The merged catalog.</returns>
    public static AgentClientCatalog Merge(AgentClientCatalog shipped, IEnumerable<AgentClientProfile> user)
    {
        ArgumentNullException.ThrowIfNull(shipped);
        ArgumentNullException.ThrowIfNull(user);
        var list = shipped.Clients.Where(IsValid).ToList();
        foreach (var profile in user.Where(IsValid))
        {
            var index = list.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list[index] = profile;
            }
            else
            {
                list.Add(profile);
            }
        }

        return shipped with { Clients = list };
    }

    /// <summary>Fills the <c>{name}</c> placeholders a value is known for; other braces stay as they are.</summary>
    /// <param name="template">The text.</param>
    /// <param name="values">The values by placeholder name.</param>
    /// <returns>The filled text.</returns>
    public static string Expand(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        return Placeholder().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
    }

    /// <summary>
    /// Expands command arguments: placeholders are filled, and an argument that ends up empty is dropped together
    /// with the option before it (for example <c>-s {scope}</c> without a scope).
    /// </summary>
    /// <param name="args">The profile's arguments.</param>
    /// <param name="values">The values.</param>
    /// <returns>The arguments to run.</returns>
    public static IReadOnlyList<string> ExpandArgs(IReadOnlyList<string>? args, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = new List<string>();
        foreach (var arg in args ?? [])
        {
            var expanded = Expand(arg, values);
            if (expanded.Length == 0 && arg.Length > 0)
            {
                if (result.Count > 0 && result[^1].StartsWith('-') && result[^1] != "--")
                {
                    result.RemoveAt(result.Count - 1);
                }

                continue;
            }

            result.Add(expanded);
        }

        return result;
    }

    /// <summary>A command line as a person types it: arguments with spaces or quotes in double quotes.</summary>
    /// <param name="command">The command.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>The line.</returns>
    public static string CommandLine(string command, IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(args);
        return string.Join(' ', new[] { command }.Concat(args).Select(QuoteArgument));
    }

    /// <summary>The placeholder values for a profile: exe, url, token, home, appdata and scope.</summary>
    /// <param name="context">The live values.</param>
    /// <param name="scope">The scope, or null for none.</param>
    /// <returns>The values by name.</returns>
    public IReadOnlyDictionary<string, string> Values(AgentClientContext context, string? scope)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["exe"] = context.Exe,
            ["url"] = context.Url,
            ["token"] = context.TokenText,
            ["home"] = Environment.Home,
            ["appdata"] = Environment.AppData,
            ["scope"] = scope ?? string.Empty,
        };
    }

    /// <summary>The configuration file of a profile on this platform, with <c>{home}</c> and <c>{appdata}</c> filled in.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The full path, or null for profiles without a file on this platform.</returns>
    public string? ConfigPath(AgentClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return ResolvePath(profile.ConfigFile);
    }

    /// <summary>
    /// The registration as a person would do it by hand (AGENT_INTEGRATION 9.4): the CLI command, "Add to &lt;file&gt;:"
    /// with the entry nested under its path (JSON or TOML), or a text row's setup text.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <param name="context">The live values.</param>
    /// <param name="scope">The scope for a CLI profile, or null for its default.</param>
    /// <returns>The text with <c>\n</c> line ends.</returns>
    public string RegistrationText(AgentClientProfile profile, AgentClientContext context, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        switch (profile.Kind)
        {
            case AgentClientKind.Cli:
                var values = Values(context, scope ?? profile.DefaultScope);
                return CommandLine(profile.Register!.Command, ExpandArgs(profile.Register.Args, values));
            case AgentClientKind.ConfigFile:
                var entry = ExpandedEntry(profile, context);
                var segments = profile.ConfigEntry!.Segments;
                var snippet = profile.IsToml
                    ? TomlTableEditor.Upsert(string.Empty, segments, entry as JsonObject ?? new JsonObject())
                    : JsoncEditor.Upsert(string.Empty, segments, entry);
                return "Add to " + (ConfigPath(profile) ?? "the client's configuration file") + ":\n" + snippet.TrimEnd('\n');
            default:
                return Expand(profile.Text ?? profile.Name, Values(context, null)).Replace("\r\n", "\n", StringComparison.Ordinal);
        }
    }

    /// <summary>The configuration entry with its placeholders filled in.</summary>
    /// <param name="profile">A configuration file profile.</param>
    /// <param name="context">The live values.</param>
    /// <returns>The value.</returns>
    public JsonNode ExpandedEntry(AgentClientProfile profile, AgentClientContext context)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        var raw = profile.ConfigEntry?.Value is { ValueKind: not JsonValueKind.Undefined } value
            ? JsonNode.Parse(value.GetRawText()) ?? new JsonObject()
            : new JsonObject();
        return ExpandNode(raw, Values(context, null));
    }

    /// <summary>Detects a client off the calling thread (a command on PATH, its folder, or its configuration file).</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>True when the client is installed.</returns>
    public Task<bool> DetectAsync(AgentClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Task.Run(() => Detect(profile));
    }

    /// <summary>Reads the remembered last registered client off the calling thread.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The profile id, or null.</returns>
    public async Task<string?> LoadLastRegisteredAsync(CancellationToken cancellationToken)
    {
        if (_stateFile is not { } file)
        {
            return LastRegisteredClientId;
        }

        LastRegisteredClientId = await Task.Run(
            () =>
            {
                try
                {
                    return File.Exists(file)
                        ? JsonSerializer.Deserialize(File.ReadAllText(file), AgentClientJsonContext.Default.AgentClientStateFile)?.LastRegisteredClientId
                        : null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    return null;
                }
            },
            cancellationToken).ConfigureAwait(false);
        return LastRegisteredClientId;
    }

    /// <summary>Remembers the client registered last (the plain briefing renders for it), off the calling thread.</summary>
    /// <param name="profileId">The profile id.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file was written.</returns>
    public async Task SaveLastRegisteredAsync(string profileId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        LastRegisteredClientId = profileId;
        if (_stateFile is not { } file)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new AgentClientStateFile(profileId), AgentClientJsonContext.Default.AgentClientStateFile);
        await Task.Run(() => WriteAtomic(file, json, bom: false, backup: false), cancellationToken).ConfigureAwait(false);
    }

    private static bool IsValid(AgentClientProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.Id) && !string.IsNullOrWhiteSpace(profile.Name);

    private static string QuoteArgument(string arg) =>
        arg.Length == 0 ? "\"\"" : arg.Any(c => char.IsWhiteSpace(c) || c == '"') ? "\"" + arg.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"" : arg;

    private static JsonNode ExpandNode(JsonNode node, IReadOnlyDictionary<string, string> values) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : ExpandNode(p.Value, values)))),
        JsonArray array => new JsonArray([.. array.Select(e => e is null ? null : ExpandNode(e, values))]),
        JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(Expand(s, values)),
        _ => node.DeepClone(),
    };

    private AgentClientCatalog LoadCore()
    {
        var problems = new List<string>();
        AgentClientCatalog shipped;
        try
        {
            shipped = _shippedFile is { } file && File.Exists(file) ? ParseCatalog(File.ReadAllText(file)) : ParseCatalog(EmbeddedProfiles());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            problems.Add("The shipped client profiles could not be read (" + ex.Message + "); the built-in copy is used.");
            shipped = ParseCatalog(EmbeddedProfiles());
        }

        var user = new List<AgentClientProfile>();
        if (_userDirectory is { } folder && Directory.Exists(folder))
        {
            foreach (var path in Directory.GetFiles(folder, "*.json").Order(StringComparer.Ordinal))
            {
                try
                {
                    user.AddRange(ParseProfiles(File.ReadAllText(path)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    problems.Add("The client profile " + path + " was skipped: " + ex.Message);
                }
            }
        }

        var dropped = shipped.Clients.Concat(user).Count(p => !IsValid(p));
        if (dropped > 0)
        {
            problems.Add(dropped.ToString(CultureInfo.InvariantCulture) + " client profile(s) without an id or a name were skipped.");
        }

        LoadProblems = problems;
        return Merge(shipped, user);
    }

    /// <summary>The embedded copy of the shipped profile file.</summary>
    /// <returns>The JSON text.</returns>
    public static string EmbeddedProfiles()
    {
        using var stream = typeof(AgentClientRegistry).Assembly.GetManifestResourceStream(EmbeddedProfileName)
            ?? throw new InvalidOperationException("The embedded client profiles " + EmbeddedProfileName + " are missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private bool Detect(AgentClientProfile profile)
    {
        if (profile.Detect?.Command is { Length: > 0 } command &&
            CliProcess.Resolve(command, Environment.PathVariable, Environment.IsWindows) is not null)
        {
            return true;
        }

        if (ResolvePath(profile.Detect?.File) is { } path && (File.Exists(path) || Directory.Exists(path)))
        {
            return true;
        }

        return ConfigPath(profile) is { } config && File.Exists(config);
    }

    private string? ResolvePath(AgentClientPath? path)
    {
        if (path?.For(Environment.Platform) is not { Length: > 0 } text)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["home"] = Environment.Home,
            ["appdata"] = Environment.AppData,
        };
        var expanded = Expand(text, values);
        if (expanded.StartsWith('~'))
        {
            expanded = Environment.Home + expanded[1..];
        }

        try
        {
            return Path.IsPathRooted(expanded) ? Path.GetFullPath(expanded) : expanded;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return expanded;
        }
    }

    // Writes a file through a temporary file and a rename; with backup the old file is copied to <file>.bak first.
    private static void WriteAtomic(string path, string text, bool bom, bool backup)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
        }

        if (backup && File.Exists(path))
        {
            File.Copy(path, path + ".bak", overwrite: true);
        }

        var temp = path + ".cutlass-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8] + ".tmp";
        try
        {
            File.WriteAllText(temp, text, new UTF8Encoding(bom));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    [GeneratedRegex(@"\{([a-z][a-z_]*)\}")]
    private static partial Regex Placeholder();
}
