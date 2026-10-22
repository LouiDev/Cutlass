namespace Cutlass.Media.Projects;

/// <summary>
/// Decides whether a media path from a project may be touched when the project opens (DESIGN 11). A project file can
/// name any path, and touching a UNC path or a mapped network drive on Windows sends the user's NTLM credentials to
/// that server. So a path on a remote volume other than the project's own is not checked until the user relinks or
/// confirms it: it counts as offline, and <see cref="RelinkScan.UnconfirmedIds"/> lists it. Remote means a UNC or
/// device path or a network drive letter on Windows, and a network file system (from <c>/proc/self/mounts</c>) on Linux.
/// Media on the same volume as the project is trusted, wherever that is.
/// </summary>
public static class MediaLocation
{
    private static readonly HashSet<string> NetworkFileSystems = new(StringComparer.Ordinal)
    {
        "nfs", "nfs4", "cifs", "smb3", "smbfs", "sshfs", "fuse.sshfs", "davfs", "fuse.davfs2", "9p", "afs", "ceph",
        "glusterfs", "fuse.glusterfs", "fuse.gvfsd-fuse", "fuse.rclone", "fuse.s3fs", "lustre", "ncpfs",
    };

    /// <summary>True when the path must not be touched before the user confirms it (see the class summary).</summary>
    /// <param name="mediaPath">The media path from the project.</param>
    /// <param name="projectPath">The project file, or null for an untitled project.</param>
    /// <returns>True for a remote path off the project's volume.</returns>
    public static bool IsUntrusted(string mediaPath, string? projectPath)
    {
        ArgumentNullException.ThrowIfNull(mediaPath);
        return OperatingSystem.IsWindows()
            ? IsUntrustedOnWindows(mediaPath, projectPath, DriveTypeOf)
            : IsUntrustedOnLinux(mediaPath, projectPath, ReadMounts());
    }

    /// <summary>The Windows rule with an injected drive type lookup. Pure apart from <paramref name="driveType"/>.</summary>
    /// <param name="mediaPath">The media path.</param>
    /// <param name="projectPath">The project file, or null.</param>
    /// <param name="driveType">The type of a drive root such as <c>Z:\</c> (no network access is needed for it).</param>
    /// <returns>True when the path must not be touched.</returns>
    public static bool IsUntrustedOnWindows(string mediaPath, string? projectPath, Func<string, DriveType> driveType)
    {
        ArgumentNullException.ThrowIfNull(mediaPath);
        ArgumentNullException.ThrowIfNull(driveType);
        var root = WindowsVolume(mediaPath);
        if (root is null)
        {
            // Relative or odd: nothing to reach over the network.
            return false;
        }

        if (projectPath is not null && WindowsVolume(projectPath) is { } projectRoot
            && string.Equals(root, projectRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return driveType(root) is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown;
    }

    /// <summary>The Linux rule with the mount table passed in. Pure.</summary>
    /// <param name="mediaPath">The media path.</param>
    /// <param name="projectPath">The project file, or null.</param>
    /// <param name="mounts">The mount points with their file system types.</param>
    /// <returns>True when the path must not be touched.</returns>
    public static bool IsUntrustedOnLinux(string mediaPath, string? projectPath, IReadOnlyList<(string MountPoint, string FileSystem)> mounts)
    {
        ArgumentNullException.ThrowIfNull(mediaPath);
        ArgumentNullException.ThrowIfNull(mounts);
        if (!mediaPath.StartsWith('/'))
        {
            return false;
        }

        var media = MountOf(mediaPath, mounts);
        if (media is null || !NetworkFileSystems.Contains(media.Value.FileSystem))
        {
            return false;
        }

        return projectPath is null || MountOf(projectPath, mounts)?.MountPoint != media.Value.MountPoint;
    }

    /// <summary>Parses <c>/proc/self/mounts</c>: the mount point (octal escapes decoded) and the file system type.</summary>
    /// <param name="text">The file content.</param>
    /// <returns>The mounts, in file order.</returns>
    public static IReadOnlyList<(string MountPoint, string FileSystem)> ParseMounts(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var mounts = new List<(string, string)>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split(' ');
            if (fields.Length >= 3)
            {
                mounts.Add((DecodeOctal(fields[1]), fields[2]));
            }
        }

        return mounts;
    }

    // The drive root (C:\) or the UNC share (\\server\share) of an absolute Windows path, or null.
    private static string? WindowsVolume(string path)
    {
        var text = path.Replace('/', '\\');
        if (text.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            text = @"\\" + text[8..];
        }
        else if (text.StartsWith(@"\\?\", StringComparison.Ordinal) && text.Length >= 6 && text[5] == ':')
        {
            text = text[4..];
        }

        if (text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            // \\server\share, or a device path (\\.\x), which counts as remote too.
            var parts = text[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? @"\\" + parts[0] + @"\" + parts[1] : @"\\" + string.Join('\\', parts);
        }

        return text.Length >= 3 && char.IsAsciiLetter(text[0]) && text[1] == ':' && text[2] == '\\'
            ? char.ToUpperInvariant(text[0]) + @":\"
            : null;
    }

    private static DriveType DriveTypeOf(string root)
    {
        try
        {
            return new DriveInfo(root).DriveType;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return DriveType.Unknown;
        }
    }

    private static (string MountPoint, string FileSystem)? MountOf(string path, IReadOnlyList<(string MountPoint, string FileSystem)> mounts)
    {
        (string, string)? best = null;
        var bestLength = -1;
        foreach (var (point, type) in mounts)
        {
            var prefix = point.EndsWith('/') ? point : point + "/";
            if ((path == point || path.StartsWith(prefix, StringComparison.Ordinal) || point == "/") && point.Length > bestLength)
            {
                best = (point, type);
                bestLength = point.Length;
            }
        }

        return best;
    }

    private static IReadOnlyList<(string MountPoint, string FileSystem)> ReadMounts()
    {
        try
        {
            return File.Exists("/proc/self/mounts") ? ParseMounts(File.ReadAllText("/proc/self/mounts")) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // /proc/mounts writes space, tab, newline and backslash as \040, \011, \012 and \134.
    private static string DecodeOctal(string field)
    {
        if (!field.Contains('\\', StringComparison.Ordinal))
        {
            return field;
        }

        var builder = new System.Text.StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length && IsOctal(field[i + 1]) && IsOctal(field[i + 2]) && IsOctal(field[i + 3]))
            {
                builder.Append((char)(((field[i + 1] - '0') * 64) + ((field[i + 2] - '0') * 8) + (field[i + 3] - '0')));
                i += 3;
            }
            else
            {
                builder.Append(field[i]);
            }
        }

        return builder.ToString();
    }

    private static bool IsOctal(char c) => c is >= '0' and <= '7';
}
