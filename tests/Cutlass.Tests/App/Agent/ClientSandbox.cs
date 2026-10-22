using Cutlass.App.Services;
using Cutlass.App.Services.Agent.Clients;
using Cutlass.Tests.Compiler;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// A fake home folder for the agent client tests (AGENT_INTEGRATION 9.3): a temporary folder used as <c>{home}</c>
/// (with <c>{appdata}</c> inside it), a PATH of only its <c>bin</c> folder, the shipped profile file from the
/// repository and a <see cref="FakeCliRunner"/>, so no test reads or writes a real client configuration or runs a
/// real client CLI.
/// </summary>
internal sealed class ClientSandbox : IDisposable
{
    /// <summary>The MCP exe the tests register.</summary>
    public const string Exe = @"C:\Program Files\Cutlass\Cutlass.Agent.Mcp.exe";

    /// <summary>Creates the sandbox.</summary>
    /// <param name="answer">The fake CLI's answers, or null to answer "not found".</param>
    /// <param name="userProfiles">The user profile folder, or null for none.</param>
    public ClientSandbox(Func<IReadOnlyList<string>, CliResult>? answer = null, string? userProfiles = null)
    {
        Home = Path.Combine(Path.GetTempPath(), "cutlass-clients-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Home);
        Runner = new FakeCliRunner(answer ?? (_ => CliResult.Missing("not found")));
        Environment = AgentClientEnvironment.Sandbox(Home);
        Registry = new AgentClientRegistry(Environment, Runner, ShippedFile, userProfiles, Path.Combine(Home, "state.json"));
    }

    /// <summary>The shipped profile file in the repository.</summary>
    public static string ShippedFile => Path.Combine(Golden.RepositoryRoot(), "src", "Cutlass.App", "Assets", "agent-clients.json");

    /// <summary>The fake home folder.</summary>
    public string Home { get; }

    /// <summary>The fake environment.</summary>
    public AgentClientEnvironment Environment { get; }

    /// <summary>The fake CLI.</summary>
    public FakeCliRunner Runner { get; }

    /// <summary>The registry under test.</summary>
    public AgentClientRegistry Registry { get; }

    /// <summary>The context the tests register with.</summary>
    public AgentClientContext Context { get; } = new(Exe, "http://localhost:47390/", "C:/Users/me/AppData/Local/Cutlass/agent/token", null, "D:/Edits");

    /// <summary>Makes a command "installed": an empty file in the fake PATH.</summary>
    /// <param name="command">The command.</param>
    public void InstallCommand(string command)
    {
        Directory.CreateDirectory(Path.Combine(Home, "bin"));
        File.WriteAllText(Path.Combine(Home, "bin", command + (OperatingSystem.IsWindows() ? ".cmd" : string.Empty)), "@echo off");
    }

    /// <summary>Writes a file below the fake home.</summary>
    /// <param name="relative">The path below the home folder.</param>
    /// <param name="text">The content.</param>
    /// <returns>The full path.</returns>
    public string Write(string relative, string text)
    {
        var path = Path.Combine(Home, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>The loaded profile with an id.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The profile.</returns>
    public async Task<AgentClientProfile> ProfileAsync(string id)
    {
        await Registry.LoadAsync(CancellationToken.None);
        return Registry.Find(id) ?? throw new InvalidOperationException("no profile " + id);
    }

    /// <summary>Removes the fake home.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(Home, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }
}
