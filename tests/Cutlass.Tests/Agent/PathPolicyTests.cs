using Cutlass.Agent.Api;
using Cutlass.Agent.Tools;
using Cutlass.Media.Platform;

namespace Cutlass.Tests.Agent;

/// <summary>File access rules (AGENT_INTEGRATION 4.6 and 7.1).</summary>
public sealed class PathPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cutlass-pathpolicy-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly AppPaths _paths;
    private readonly string _allowed;
    private readonly string _outside;

    public PathPolicyTests()
    {
        _allowed = Path.Combine(_root, "Footage");
        _outside = Path.Combine(_root, "Private");
        Directory.CreateDirectory(_allowed);
        Directory.CreateDirectory(_outside);
        _paths = new AppPaths(Path.Combine(_root, "cfg", "settings.json"), Path.Combine(_root, "data"), Path.Combine(_root, "cache", "v1"), OperatingSystem.IsWindows());
        Directory.CreateDirectory(_paths.DataDirectory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
    }

    private static ApiException Refused(Func<string> check)
    {
        var ex = Assert.Throws<ApiException>(check);
        return ex;
    }

    [Fact]
    public void APathInsideAnAllowedFolderPasses()
    {
        var file = Path.Combine(_allowed, "sub", "clip.mp4");
        Assert.Equal(Path.GetFullPath(file), PathPolicy.Check(file, [_allowed], _paths));
        Assert.Equal(Path.GetFullPath(_allowed), PathPolicy.Check(_allowed, [_allowed + Path.DirectorySeparatorChar], _paths));
    }

    [Fact]
    public void APathOutsideIsForbiddenWithTheHint()
    {
        var file = Path.Combine(_outside, "secret.txt");
        var ex = Refused(() => PathPolicy.Check(file, [_allowed], _paths));
        Assert.Equal(ErrorCode.Forbidden, ex.Code);
        Assert.Equal(file, ex.Error.Detail("path"));
        Assert.Contains("Settings > Agents", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DotDotCannotEscape()
    {
        var sneaky = Path.Combine(_allowed, "..", "Private", "secret.txt");
        Assert.Equal(ErrorCode.Forbidden, Refused(() => PathPolicy.Check(sneaky, [_allowed], _paths)).Code);
        var sibling = _allowed + "Extra" + Path.DirectorySeparatorChar + "x.mp4";
        Assert.Equal(ErrorCode.Forbidden, Refused(() => PathPolicy.Check(sibling, [_allowed], _paths)).Code);
    }

    [Fact]
    public void CaseFollowsThePlatform()
    {
        var upper = Path.Combine(_allowed.ToUpperInvariant(), "clip.mp4");
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(PathPolicy.Check(upper, [_allowed], _paths));
        }
        else
        {
            Assert.Equal(ErrorCode.Forbidden, Refused(() => PathPolicy.Check(upper, [_allowed], _paths)).Code);
        }
    }

    [Fact]
    public void ALinkPointingOutIsResolvedAndRefused()
    {
        var link = Path.Combine(_allowed, "escape");
        try
        {
            Directory.CreateSymbolicLink(link, _outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Creating symbolic links needs a privilege on Windows without developer mode; nothing to test then.
            return;
        }

        var through = Path.Combine(link, "secret.txt");
        Assert.Equal(ErrorCode.Forbidden, Refused(() => PathPolicy.Check(through, [_allowed], _paths)).Code);
        Assert.Equal(Path.Combine(_outside, "secret.txt"), PathPolicy.Check(through, [_outside], _paths));
    }

    [Theory]
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\Windows\win.ini")]
    [InlineData("/dev/sda")]
    [InlineData("relative/clip.mp4")]
    public void DeviceAndRelativePathsAreRefused(string path)
    {
        Assert.Equal(ErrorCode.Forbidden, Refused(() => PathPolicy.Check(path, [_allowed, "/"], _paths)).Code);
    }

    [Fact]
    public void TheDataCacheAndSettingsFoldersAreNeverAllowed()
    {
        foreach (var folder in new[] { _paths.DataDirectory, _paths.CacheRoot, _paths.SettingsDirectory })
        {
            var file = Path.Combine(folder, "agent", "token");
            Assert.Equal(ErrorCode.Forbidden, Refused(() => PathPolicy.Check(file, [_root], _paths)).Code);
        }
    }

    [Fact]
    public void AnEmptyPathIsAnInvalidArgument()
    {
        Assert.Equal(ErrorCode.InvalidArgument, Refused(() => PathPolicy.Check(" ", [_allowed], _paths)).Code);
    }
}
