using Cutlass.Agent.Http;
using Cutlass.Media.Platform;

namespace Cutlass.Tests.Agent;

/// <summary>AGENT_INTEGRATION 8 "Two writers on one file": the check behind <c>--headless</c> and <c>serve</c>.</summary>
public sealed class ProjectOwnerGuardTests
{
    private static readonly AppPaths WindowsPaths = new(@"C:\s\settings.json", @"C:\d", @"C:\c", isWindows: true);
    private static readonly AppPaths LinuxPaths = new("/s/settings.json", "/d", "/c", isWindows: false);

    private static EndpointInfo App(string? project, string kind = "app") => new()
    {
        BaseUrl = "http://localhost:47390/",
        Pid = 4242,
        HostKind = kind,
        TokenFile = "C:/d/agent/token",
        ProjectPath = project,
    };

    [Fact]
    public void ALiveAppWithTheSameProjectRefusesWithAClearMessage()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var message = ProjectOwnerGuard.Check(App("D:/Edits/Ep07.cutlass"), WindowsPaths, @"d:\edits\ep07.cutlass", _ => true);
        Assert.NotNull(message);
        Assert.Contains("the running Cutlass app", message, StringComparison.Ordinal);
        Assert.Contains("pid 4242", message, StringComparison.Ordinal);
        Assert.Contains("http://localhost:47390/", message, StringComparison.Ordinal);
        Assert.Contains("close the project", message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherHeadlessHostIsNamedAsSuch()
    {
        var project = Path.Combine(Path.GetTempPath(), "ep07.cutlass");
        var paths = OperatingSystem.IsWindows() ? WindowsPaths : LinuxPaths;
        var message = ProjectOwnerGuard.Check(App(project.Replace(Path.DirectorySeparatorChar, '/'), "headless"), paths, project, _ => true);
        Assert.NotNull(message);
        Assert.Contains("another headless Cutlass host", message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFileADeadOwnerAnUntitledAppOrAnotherProjectAreAllowed()
    {
        var project = Path.Combine(Path.GetTempPath(), "ep07.cutlass");
        var paths = OperatingSystem.IsWindows() ? WindowsPaths : LinuxPaths;
        Assert.Null(ProjectOwnerGuard.Check(null, paths, project, _ => true));
        Assert.Null(ProjectOwnerGuard.Check(App(project), paths, project, _ => false));
        Assert.Null(ProjectOwnerGuard.Check(App(null), paths, project, _ => true));
        Assert.Null(ProjectOwnerGuard.Check(App(Path.Combine(Path.GetTempPath(), "ep08.cutlass")), paths, project, _ => true));
    }

    [Fact]
    public void LinuxComparesPathsCaseSensitively()
    {
        Assert.Null(ProjectOwnerGuard.Check(App("/edits/Ep07.cutlass"), LinuxPaths, "/edits/ep07.cutlass", _ => true));
        if (!OperatingSystem.IsWindows())
        {
            Assert.NotNull(ProjectOwnerGuard.Check(App("/edits/ep07.cutlass"), LinuxPaths, "/edits/ep07.cutlass", _ => true));
        }
    }

    [Fact]
    public void TheFileOverloadReadsTheEndpointFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cutlass-owner-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = Path.Combine(dir, "p.cutlass");
            var endpoint = new EndpointFile(dir);
            var paths = OperatingSystem.IsWindows() ? WindowsPaths : LinuxPaths;
            Assert.Null(ProjectOwnerGuard.Check(endpoint, paths, project));

            // This process is never "another" process, so its own file does not refuse.
            endpoint.Write(EndpointFile.ForThisProcess("http://localhost:47390/", "app", Path.Combine(dir, "token"), project));
            Assert.Null(ProjectOwnerGuard.Check(endpoint, paths, project));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
