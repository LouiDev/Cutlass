using Cutlass.Agent.Api;
using Cutlass.Agent.Hosting;
using Cutlass.Core.Model;
using Cutlass.Media.Platform;

namespace Cutlass.Agent.Tools;

/// <summary>
/// The file access rules of AGENT_INTEGRATION 4.6. A path is taken with <see cref="Path.GetFullPath(string)"/>, links
/// on the file and its parents are resolved, and it must lie inside an allowed folder (compared with
/// <see cref="AppPaths.PathComparison"/>). Device paths and the data, cache and settings folders are always refused.
/// Read folders: the folders of the project's media and of the project, Settings "Agent may read from" and
/// <c>--allow-read</c>; the write folders are readable too (unspecified; an agent may import what it exported). Write
/// folders: the export folder (else the project folder), Settings "Agent may write to" and <c>--allow-write</c>;
/// saving to the current project path is always allowed.
/// </summary>
public sealed class PathPolicy
{
    private readonly IAgentHost _host;

    /// <summary>Creates the policy for a host.</summary>
    /// <param name="host">The host (for its policy and paths).</param>
    public PathPolicy(IAgentHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <summary>Checks a path the agent wants to read (open, import, fonts).</summary>
    /// <param name="path">The path.</param>
    /// <param name="project">The current project (its media folders are readable).</param>
    /// <param name="projectPath">The project file, or null.</param>
    /// <returns>The resolved full path.</returns>
    /// <exception cref="ApiException"><c>forbidden</c> or <c>invalid_argument</c>.</exception>
    public string CheckRead(string path, Project project, string? projectPath)
    {
        ArgumentNullException.ThrowIfNull(project);
        var policy = _host.Policy;
        var folders = new List<string>();
        folders.AddRange(project.Media.Select(m => Path.GetDirectoryName(m.Path)).OfType<string>());
        if (projectPath is not null && Path.GetDirectoryName(projectPath) is { } dir)
        {
            folders.Add(dir);
        }

        folders.AddRange(policy.ReadFolders);
        folders.AddRange(WriteFolders(projectPath));
        return Check(path, folders, _host.Services.Paths);
    }

    /// <summary>Checks a path the agent wants to write (save as, exports).</summary>
    /// <param name="path">The path.</param>
    /// <param name="projectPath">The project file, or null (saving to it is always allowed).</param>
    /// <returns>The resolved full path.</returns>
    /// <exception cref="ApiException"><c>forbidden</c> or <c>invalid_argument</c>.</exception>
    public string CheckWrite(string path, string? projectPath)
    {
        var paths = _host.Services.Paths;
        if (projectPath is not null && !string.IsNullOrWhiteSpace(path) && !IsDevicePath(path)
            && string.Equals(SafeFullPath(path), SafeFullPath(projectPath), paths.PathComparison))
        {
            return Path.GetFullPath(projectPath);
        }

        return Check(path, WriteFolders(projectPath), paths);
    }

    /// <summary>
    /// The core check: the path, resolved, must lie inside one of the folders, and outside the data, cache and
    /// settings folders.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="allowedFolders">The allowed folders.</param>
    /// <param name="paths">The app paths (comparison and the refused folders).</param>
    /// <returns>The resolved full path.</returns>
    /// <exception cref="ApiException"><c>forbidden</c> or <c>invalid_argument</c>.</exception>
    public static string Check(string path, IEnumerable<string> allowedFolders, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(allowedFolders);
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw ApiException.InvalidArgument("path", "The path is empty.");
        }

        if (IsDevicePath(path) || !Path.IsPathFullyQualified(path))
        {
            throw Forbidden(path, "Only absolute file paths are allowed; device and relative paths are refused.");
        }

        string resolved;
        try
        {
            resolved = RealPath(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            throw ApiException.InvalidArgument("path", $"'{path}' is not a valid path: {ex.Message}");
        }

        foreach (var refused in new[] { paths.DataDirectory, paths.CacheRoot, paths.SettingsDirectory })
        {
            if (IsInside(resolved, RealPath(refused), paths.PathComparison))
            {
                throw Forbidden(path, "Cutlass's own data, cache and settings folders are never accessible to agents.");
            }
        }

        foreach (var folder in allowedFolders)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                continue;
            }

            string real;
            try
            {
                real = RealPath(Path.GetFullPath(folder));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
            {
                continue;
            }

            if (IsInside(resolved, real, paths.PathComparison))
            {
                return resolved;
            }
        }

        throw Forbidden(path, "The path is outside the folders agents may use. Ask the user to add this folder under Settings > Agents.");
    }

    /// <summary>True for Win32 device and extended paths (<c>\\.\</c>, <c>\\?\</c>) and for <c>/dev/</c> and <c>/proc/</c>.</summary>
    /// <param name="path">The path.</param>
    /// <returns>True for a device path.</returns>
    public static bool IsDevicePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var p = path.Replace('/', '\\');
        return p.StartsWith(@"\\.\", StringComparison.Ordinal) || p.StartsWith(@"\\?\", StringComparison.Ordinal)
            || path.StartsWith("/dev/", StringComparison.Ordinal) || path.StartsWith("/proc/", StringComparison.Ordinal)
            || path.Contains('\0', StringComparison.Ordinal);
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or lies inside it.</summary>
    /// <param name="path">A full path.</param>
    /// <param name="folder">A full folder path.</param>
    /// <param name="comparison">The path comparison.</param>
    /// <returns>True when inside.</returns>
    public static bool IsInside(string path, string folder, StringComparison comparison)
    {
        var f = Path.TrimEndingDirectorySeparator(folder);
        var p = Path.TrimEndingDirectorySeparator(path);
        if (string.Equals(p, f, comparison))
        {
            return true;
        }

        return p.StartsWith(f + Path.DirectorySeparatorChar, comparison) || p.StartsWith(f + Path.AltDirectorySeparatorChar, comparison);
    }

    /// <summary>Resolves symbolic links and junctions on the path and each of its parents.</summary>
    /// <param name="fullPath">A full path.</param>
    /// <returns>The real path (unchanged parts stay as given).</returns>
    public static string RealPath(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var rest = fullPath[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var guard = 0; guard < rest.Length; guard++)
        {
            current = Path.Combine(current, rest[guard]);
            FileSystemInfo? info = Directory.Exists(current) ? new DirectoryInfo(current) : File.Exists(current) ? new FileInfo(current) : null;
            if (info?.LinkTarget is not null && info.ResolveLinkTarget(true) is { } target)
            {
                current = Path.GetFullPath(target.FullName);
            }
        }

        return current.Length == 0 ? fullPath : current;
    }

    private IEnumerable<string> WriteFolders(string? projectPath)
    {
        var policy = _host.Policy;
        var list = new List<string>();
        if (policy.ExportFolder is { } export)
        {
            list.Add(export);
        }
        else if (projectPath is not null && Path.GetDirectoryName(projectPath) is { } dir)
        {
            list.Add(dir);
        }

        list.AddRange(policy.WriteFolders);
        return list;
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return path;
        }
    }

    private static ApiException Forbidden(string path, string message) =>
        new(ErrorCode.Forbidden, message, new Dictionary<string, string> { ["path"] = path });
}
