using Cutlass.Agent.Http;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Platform;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>
/// AGENT_INTEGRATION 8 "Two writers on one file": the built <c>Cutlass.Agent.Mcp</c> in <c>--headless</c> and
/// <c>serve</c> mode refuses a project that a live process names in this user's <c>endpoint.json</c>. The test process
/// plays the live app: it writes the real endpoint file naming itself, which the exe sees as another live process.
/// </summary>
public sealed class McpProjectOwnerTests
{
    [Theory]
    [InlineData("--headless")]
    [InlineData("serve")]
    public async Task HeadlessModesRefuseAProjectALiveAppHasOpen(string mode)
    {
        var paths = AppPaths.ForCurrentPlatform();
        var endpoint = new EndpointFile(paths.AgentDirectory);
        if (File.Exists(endpoint.FilePath))
        {
            // A real app or server (or its leftover) owns the file; never overwrite it. Nothing to check here.
            return;
        }

        using var temp = new TempDirectory("cutlass-mcp-owner");
        var project = temp.Combine("open-in-app.cutlass");
        endpoint.Write(EndpointFile.ForThisProcess("http://localhost:47391/", "app", temp.Combine("token"), project));
        try
        {
            string[] args = mode == "serve"
                ? [McpEndToEndTests.McpDllPath(), "serve", "--project", project, "--create", "--port", "47398"]
                : [McpEndToEndTests.McpDllPath(), "--headless", "--project", project, "--create"];
            var result = await TestProcess.RunAsync("dotnet", args, temp.Path, TimeSpan.FromSeconds(60));

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("is open in the running Cutlass app", result.Stderr, StringComparison.Ordinal);
            Assert.False(File.Exists(project), "The refused host must not create the project file.");
            Assert.Equal(string.Empty, result.Stdout.Trim());
        }
        finally
        {
            endpoint.DeleteIfOwned();
        }

        Assert.False(File.Exists(endpoint.FilePath));
    }
}
