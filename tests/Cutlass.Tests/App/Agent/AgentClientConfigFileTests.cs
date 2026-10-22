using System.Text;
using System.Text.Json;
using Cutlass.App.Services.Agent.Clients;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// AGENT_INTEGRATION 9.3, the configuration file kind, on a fake home: the probe states (not installed, not
/// registered, current, stale, foreign, a broken file), register, update and remove with the other servers, the
/// comments and the formatting of the file kept, a <c>.bak</c> of the previous file, the UTF-8 BOM kept, and the TOML
/// profile, whose other tables stay byte for byte (a register then remove gives the original file back).
/// </summary>
public sealed class AgentClientConfigFileTests
{
    private const string Exe = ClientSandbox.Exe;
    private const string ExeJson = "C:\\\\Program Files\\\\Cutlass\\\\Cutlass.Agent.Mcp.exe";

    [Fact]
    public async Task TheProbeStatesOfAJsonClient()
    {
        using var box = new ClientSandbox();
        var cursor = await box.ProfileAsync("cursor");
        var path = Path.GetFullPath(Path.Combine(box.Home, ".cursor", "mcp.json"));

        var none = await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.NotInstalled, none.State);
        Assert.False(none.Installed);

        Directory.CreateDirectory(Path.Combine(box.Home, ".cursor"));
        var empty = await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.NotRegistered, empty.State);
        Assert.Equal(path, empty.ConfigPath);
        Assert.True(empty.Installed);

        box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"other\": { \"command\": \"npx\" } } }");
        Assert.Equal(AgentClientState.NotRegistered, (await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None)).State);

        box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"cutlass\": { \"command\": \"" + ExeJson + "\", \"args\": [], \"env\": {} } } }");
        var current = await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.Current, current.State);
        Assert.Equal("\"" + Exe + "\"", current.Detail);

        // A missing empty args list still matches; other args do not.
        box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"cutlass\": { \"command\": \"" + ExeJson + "\" } } }");
        Assert.Equal(AgentClientState.Current, (await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None)).State);

        box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"cutlass\": { \"command\": \"D:/old/Cutlass.Agent.Mcp.exe\", \"args\": [] } } }");
        var stale = await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.Stale, stale.State);
        Assert.Equal("D:/old/Cutlass.Agent.Mcp.exe", stale.Detail);

        box.Write(".cursor/mcp.json", "{ \"mcpServers\": { \"cutlass\": { \"command\": \"npx\", \"args\": [\"cutlass-cli\"] } } }");
        var foreign = await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.Foreign, foreign.State);
        Assert.Equal("npx cutlass-cli", foreign.Detail);

        box.Write(".cursor/mcp.json", "{ \"mcpServers\": ");
        var broken = await box.Registry.ProbeAsync(cursor, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.Error, broken.State);
        Assert.StartsWith("The configuration file cannot be read", broken.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterUpdateAndRemoveKeepTheRestOfTheFile()
    {
        using var box = new ClientSandbox();
        var cursor = await box.ProfileAsync("cursor");
        var original = "{\n  // My servers.\n  \"mcpServers\": {\n    \"other\": { \"command\": \"npx\", \"args\": [\"thing\"] },\n  },\n  \"theme\": \"dark\"\n}\n";
        var path = box.Write(".cursor/mcp.json", original);

        var register = await box.Registry.RegisterAsync(cursor, box.Context, null, CancellationToken.None);
        Assert.True(register.Succeeded);
        Assert.Equal(AgentClientState.Current, register.Probe.State);
        Assert.Contains("Wrote mcpServers.cutlass to " + Path.GetFullPath(path), register.Output, StringComparison.Ordinal);
        Assert.Equal(original, await File.ReadAllTextAsync(path + ".bak"));
        var written = await File.ReadAllTextAsync(path);
        Assert.Equal(
            "{\n  // My servers.\n  \"mcpServers\": {\n    \"other\": { \"command\": \"npx\", \"args\": [\"thing\"] },\n    \"cutlass\": {\n      \"command\": \"" + ExeJson + "\",\n      \"args\": []\n    },\n  },\n  \"theme\": \"dark\"\n}\n",
            written);

        // Registering again changes nothing.
        var again = await box.Registry.RegisterAsync(cursor, box.Context, null, CancellationToken.None);
        Assert.True(again.Succeeded);
        Assert.Contains("already holds this entry", again.Output, StringComparison.Ordinal);
        Assert.Equal(written, await File.ReadAllTextAsync(path));

        // Another install's exe: stale, then Update points it here.
        var otherContext = box.Context with { Exe = @"D:\new\Cutlass.Agent.Mcp.exe" };
        Assert.Equal(AgentClientState.Stale, (await box.Registry.ProbeAsync(cursor, otherContext, CancellationToken.None)).State);
        var update = await box.Registry.RegisterAsync(cursor, otherContext, null, CancellationToken.None);
        Assert.Equal(AgentClientState.Current, update.Probe.State);
        Assert.Contains("D:\\\\new\\\\Cutlass.Agent.Mcp.exe", await File.ReadAllTextAsync(path), StringComparison.Ordinal);
        Assert.Contains("\"other\": { \"command\": \"npx\", \"args\": [\"thing\"] }", await File.ReadAllTextAsync(path), StringComparison.Ordinal);

        // Remove: the file is the original again.
        var remove = await box.Registry.RemoveAsync(cursor, otherContext, CancellationToken.None);
        Assert.True(remove.Succeeded);
        Assert.Equal(AgentClientState.NotRegistered, remove.Probe.State);
        Assert.Equal(original, await File.ReadAllTextAsync(path));
        var nothing = await box.Registry.RemoveAsync(cursor, otherContext, CancellationToken.None);
        Assert.True(nothing.Succeeded);
        Assert.Contains("nothing was changed", nothing.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterCreatesAMissingFileAndItsFolder()
    {
        using var box = new ClientSandbox();
        var profile = await box.ProfileAsync("continue");
        Directory.CreateDirectory(Path.Combine(box.Home, ".continue"));
        var result = await box.Registry.RegisterAsync(profile, box.Context, null, CancellationToken.None);
        Assert.True(result.Succeeded);
        var path = Path.Combine(box.Home, ".continue", "mcpServers", "cutlass.json");
        Assert.False(File.Exists(path + ".bak"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(Exe, document.RootElement.GetProperty("mcpServers").GetProperty("cutlass").GetProperty("command").GetString());
        Assert.Equal(AgentClientState.Current, result.Probe.State);
    }

    [Fact]
    public async Task ABrokenFileIsNotChangedAndABomIsKept()
    {
        using var box = new ClientSandbox();
        var gemini = await box.ProfileAsync("gemini-cli");
        var path = box.Write(".gemini/settings.json", "{ \"theme\": ");
        var result = await box.Registry.RegisterAsync(gemini, box.Context, null, CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Contains("is not valid JSON", result.Output, StringComparison.Ordinal);
        Assert.Equal("{ \"theme\": ", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".bak"));

        await File.WriteAllBytesAsync(path, [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes("{ \"theme\": \"Dark\" }")]);
        Assert.True((await box.Registry.RegisterAsync(gemini, box.Context, null, CancellationToken.None)).Succeeded);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("\"theme\": \"Dark\", \"mcpServers\": {", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task VsCodeWritesItsOwnShape()
    {
        using var box = new ClientSandbox();
        var vscode = await box.ProfileAsync("vscode");
        var path = box.Registry.ConfigPath(vscode)!;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.Equal(AgentClientState.NotRegistered, (await box.Registry.ProbeAsync(vscode, box.Context, CancellationToken.None)).State);
        Assert.True((await box.Registry.RegisterAsync(vscode, box.Context, null, CancellationToken.None)).Succeeded);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var entry = document.RootElement.GetProperty("servers").GetProperty("cutlass");
        Assert.Equal("stdio", entry.GetProperty("type").GetString());

        // Without "type" the entry is someone else's.
        box.Write(Path.GetRelativePath(box.Home, path), "{ \"servers\": { \"cutlass\": { \"command\": \"" + ExeJson + "\", \"args\": [] } } }");
        Assert.Equal(AgentClientState.Stale, (await box.Registry.ProbeAsync(vscode, box.Context, CancellationToken.None)).State);
    }

    [Fact]
    public async Task TheTomlClientKeepsOtherTablesByteForByte()
    {
        using var box = new ClientSandbox();
        var codex = await box.ProfileAsync("codex-cli");
        var original = "# Codex settings\nmodel = \"o3\"\n\n[mcp_servers.other]\ncommand = \"npx\"\nargs = [\"-y\", \"thing\"]   # a comment\n\n[profiles.fast]\nmodel = 'o4-mini'\n";
        var path = box.Write(".codex/config.toml", original);
        Assert.Equal(AgentClientState.NotRegistered, (await box.Registry.ProbeAsync(codex, box.Context, CancellationToken.None)).State);

        var register = await box.Registry.RegisterAsync(codex, box.Context, null, CancellationToken.None);
        Assert.True(register.Succeeded);
        Assert.Equal(AgentClientState.Current, register.Probe.State);
        Assert.Equal(
            original + "\n[mcp_servers.cutlass]\ncommand = \"C:\\\\Program Files\\\\Cutlass\\\\Cutlass.Agent.Mcp.exe\"\nargs = []\n",
            await File.ReadAllTextAsync(path));
        Assert.Equal(original, await File.ReadAllTextAsync(path + ".bak"));

        var remove = await box.Registry.RemoveAsync(codex, box.Context, CancellationToken.None);
        Assert.True(remove.Succeeded);
        Assert.Equal(original, await File.ReadAllTextAsync(path));

        // A stale table in the middle is updated in place, its extra keys stay.
        var stale = "[mcp_servers.cutlass]\ncommand = 'D:\\old\\Cutlass.Agent.Mcp.exe'\nargs = [\n  \"--headless\",\n]\nenv = { A = \"1\" }\n\n[other]\nx = 1\n";
        box.Write(".codex/config.toml", stale);
        var probe = await box.Registry.ProbeAsync(codex, box.Context, CancellationToken.None);
        Assert.Equal(AgentClientState.Stale, probe.State);
        Assert.Equal("D:\\old\\Cutlass.Agent.Mcp.exe --headless", probe.Detail);
        await box.Registry.RegisterAsync(codex, box.Context, null, CancellationToken.None);
        Assert.Equal(
            "[mcp_servers.cutlass]\ncommand = \"C:\\\\Program Files\\\\Cutlass\\\\Cutlass.Agent.Mcp.exe\"\nargs = []\nenv = { A = \"1\" }\n\n[other]\nx = 1\n",
            await File.ReadAllTextAsync(path));
    }
}
