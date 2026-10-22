using System.Globalization;
using Cutlass.Media.Platform;

namespace Cutlass.Agent.Http;

/// <summary>
/// AGENT_INTEGRATION 8 "Two writers on one file": <c>Cutlass.Agent.Mcp --headless</c> and <c>serve</c> refuse a
/// project that a live Cutlass process names in <c>endpoint.json</c>, because two hosts saving one file overwrite
/// each other's work.
/// </summary>
public static class ProjectOwnerGuard
{
    /// <summary>Checks the endpoint file of this user.</summary>
    /// <param name="endpoint">The endpoint file.</param>
    /// <param name="paths">The app paths (path comparison rules).</param>
    /// <param name="projectPath">The project the headless host would open.</param>
    /// <returns>The refusal message, or null when no live process has the project open.</returns>
    public static string? Check(EndpointFile endpoint, AppPaths paths, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return Check(endpoint.Read(), paths, projectPath, EndpointFile.IsLiveOther);
    }

    /// <summary>Checks one endpoint record.</summary>
    /// <param name="info">The record, or null when there is no endpoint file.</param>
    /// <param name="paths">The app paths (path comparison rules).</param>
    /// <param name="projectPath">The project the headless host would open.</param>
    /// <param name="isLiveOther">True when the record names a live process other than this one.</param>
    /// <returns>The refusal message, or null when no live process has the project open.</returns>
    public static string? Check(EndpointInfo? info, AppPaths paths, string projectPath, Func<EndpointInfo, bool> isLiveOther)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(isLiveOther);
        if (info?.ProjectPath is not { Length: > 0 } open || !paths.PathsEqual(open, projectPath) || !isLiveOther(info))
        {
            return null;
        }

        var owner = string.Equals(info.HostKind, "app", StringComparison.Ordinal)
            ? "the running Cutlass app"
            : "another headless Cutlass host";
        return $"{Path.GetFullPath(projectPath)} is open in {owner} (pid {info.Pid.ToString(CultureInfo.InvariantCulture)}, serving agents at {info.BaseUrl}). "
            + "Two writers on one project would overwrite each other's saves. Edit it there instead (Cutlass.Agent.Mcp without "
            + "--headless proxies to it), or close the project in that process first.";
    }
}
