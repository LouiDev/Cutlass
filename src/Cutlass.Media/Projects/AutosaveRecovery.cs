using System.Diagnostics;
using System.Globalization;
using Cutlass.Core.Serialization;

namespace Cutlass.Media.Projects;

/// <summary>
/// Finds autosaves worth recovering (DESIGN 11): a project's <c>&lt;name&gt;.cutlass.autosave</c> that is newer than
/// the project file, and every untitled project's <c>&lt;data&gt;/autosave/untitled-&lt;id&gt;.cutlass</c>. A clean save
/// or close deletes the autosave, so one that is left over means the app did not close cleanly. File system members
/// are cheap metadata lookups but still I/O: call them off the UI thread.
/// </summary>
/// <remarks>
/// <b>Owner locks.</b> A running instance keeps a sidecar <c>&lt;autosave&gt;.lock</c> next to every autosave it writes,
/// holding its process id and start time (<see cref="FormatOwner"/>). An autosave whose owner is still alive belongs to
/// a running instance: it is not left over, so it is never offered for recovery (recovering it would make two
/// writers, and discarding it would delete live crash protection). The lock of a process that ended (a crash) is
/// stale and ignored.
/// </remarks>
public static class AutosaveRecovery
{
    /// <summary>The file name prefix of untitled autosaves.</summary>
    public const string UntitledPrefix = "untitled-";

    /// <summary>The suffix of an autosave's owner lock: <c>&lt;autosave&gt;.lock</c>.</summary>
    public const string LockSuffix = ".lock";

    /// <summary>The owner lock of an autosave.</summary>
    /// <param name="autosavePath">The autosave file.</param>
    /// <returns><c>&lt;autosave&gt;.lock</c>.</returns>
    public static string LockPath(string autosavePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autosavePath);
        return autosavePath + LockSuffix;
    }

    /// <summary>The content of a lock owned by this process: its id and start time.</summary>
    /// <returns>The lock text.</returns>
    public static string CurrentOwner()
    {
        using var self = Process.GetCurrentProcess();
        return FormatOwner(Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks);
    }

    /// <summary>Formats a lock owner: <c>&lt;pid&gt; &lt;start ticks UTC&gt;</c>, invariant culture.</summary>
    /// <param name="processId">The owner's process id.</param>
    /// <param name="startTicks">The owner's start time in UTC ticks (tells a reused process id apart).</param>
    /// <returns>The lock text.</returns>
    public static string FormatOwner(int processId, long startTicks) =>
        string.Create(CultureInfo.InvariantCulture, $"{processId} {startTicks}");

    /// <summary>Parses a lock written by <see cref="FormatOwner"/>.</summary>
    /// <param name="text">The lock text.</param>
    /// <param name="processId">The owner's process id.</param>
    /// <param name="startTicks">The owner's start time in UTC ticks.</param>
    /// <returns>False when the text is not a lock.</returns>
    public static bool TryParseOwner(string? text, out int processId, out long startTicks)
    {
        processId = 0;
        startTicks = 0;
        var parts = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
            long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out startTicks);
    }

    /// <summary>
    /// True when the process is running and started at <paramref name="startTicks"/> (within a second, so a reused id
    /// counts as dead). A process whose start time cannot be read (one of another user) counts as dead too: the
    /// autosave folders are per user.
    /// </summary>
    /// <param name="processId">The process id.</param>
    /// <param name="startTicks">The expected start time in UTC ticks.</param>
    /// <returns>True when that process is alive.</returns>
    public static bool IsProcessAlive(int processId, long startTicks)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            var started = process.StartTime.ToUniversalTime().Ticks;
            return Math.Abs(started - startTicks) < TimeSpan.TicksPerSecond;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// True when a running instance (this one or another) owns the autosave: its lock names a process that is alive.
    /// A missing or damaged lock means no live owner; a lock that cannot be read is being written by its owner.
    /// </summary>
    /// <param name="autosavePath">The autosave file.</param>
    /// <param name="isAlive">The liveness check, normally <see cref="IsProcessAlive"/>.</param>
    /// <returns>True when a live instance owns it.</returns>
    public static bool HasLiveOwner(string autosavePath, Func<int, long, bool> isAlive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autosavePath);
        ArgumentNullException.ThrowIfNull(isAlive);
        try
        {
            var lockPath = LockPath(autosavePath);
            return File.Exists(lockPath) && TryParseOwner(File.ReadAllText(lockPath), out var pid, out var ticks) && isAlive(pid, ticks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>The path of an untitled project's autosave.</summary>
    /// <param name="autosaveDirectory">The untitled autosave folder (<c>AppPaths.AutosaveDirectory</c>).</param>
    /// <param name="id">A unique id for the untitled project (letters and digits).</param>
    /// <returns>The full path, <c>untitled-&lt;id&gt;.cutlass</c>.</returns>
    public static string UntitledPath(string autosaveDirectory, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autosaveDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Path.Combine(Path.GetFullPath(autosaveDirectory), UntitledPrefix + id + ProjectSerializer.ProjectExtension);
    }

    /// <summary>
    /// The recovery rule (DESIGN 11): an autosave is offered when it is newer than its project, or when the project
    /// file does not exist (any more). Pure.
    /// </summary>
    /// <param name="autosaveUtc">The autosave's last write time.</param>
    /// <param name="projectUtc">The project's last write time, or null when the project file is missing.</param>
    /// <returns>True when recovery should be offered.</returns>
    public static bool ShouldOffer(DateTime autosaveUtc, DateTime? projectUtc) =>
        projectUtc is not { } project || autosaveUtc > project;

    /// <summary>
    /// The recoverable autosave of a project, or null when there is none, it is not newer, or a running instance owns
    /// it.
    /// </summary>
    /// <param name="projectPath">The project file.</param>
    /// <param name="isOwnerAlive">The lock liveness check, or null for <see cref="IsProcessAlive"/>.</param>
    /// <returns>The candidate, or null.</returns>
    public static RecoverableProject? ForProject(string projectPath, Func<int, long, bool>? isOwnerAlive = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        try
        {
            var full = Path.GetFullPath(projectPath);
            var autosave = new FileInfo(ProjectStore.AutosavePath(full));
            if (!autosave.Exists || autosave.Length == 0 || HasLiveOwner(autosave.FullName, isOwnerAlive ?? IsProcessAlive))
            {
                return null;
            }

            var project = new FileInfo(full);
            return ShouldOffer(autosave.LastWriteTimeUtc, project.Exists ? project.LastWriteTimeUtc : null)
                ? new RecoverableProject(autosave.FullName, full, autosave.LastWriteTimeUtc)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Every untitled autosave in the folder that no running instance owns, newest first.</summary>
    /// <param name="autosaveDirectory">The untitled autosave folder.</param>
    /// <param name="isOwnerAlive">The lock liveness check, or null for <see cref="IsProcessAlive"/>.</param>
    /// <returns>The candidates (empty when the folder does not exist).</returns>
    public static IReadOnlyList<RecoverableProject> FindUntitled(string autosaveDirectory, Func<int, long, bool>? isOwnerAlive = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(autosaveDirectory);
        var alive = isOwnerAlive ?? IsProcessAlive;
        try
        {
            var folder = new DirectoryInfo(autosaveDirectory);
            if (!folder.Exists)
            {
                return [];
            }

            return [.. folder.EnumerateFiles(UntitledPrefix + "*" + ProjectSerializer.ProjectExtension)
                .Where(f => f.Length > 0 && f.Name.EndsWith(ProjectSerializer.ProjectExtension, StringComparison.Ordinal))
                .Where(f => !HasLiveOwner(f.FullName, alive))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new RecoverableProject(f.FullName, null, f.LastWriteTimeUtc))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Everything recoverable at startup: the untitled autosaves plus the newer autosaves of the given projects (the
    /// recent list), newest first, without duplicates.
    /// </summary>
    /// <param name="autosaveDirectory">The untitled autosave folder.</param>
    /// <param name="projects">Project files to check (for example the recent list).</param>
    /// <param name="isOwnerAlive">The lock liveness check, or null for <see cref="IsProcessAlive"/>.</param>
    /// <returns>The candidates.</returns>
    public static IReadOnlyList<RecoverableProject> FindAll(string autosaveDirectory, IEnumerable<string> projects, Func<int, long, bool>? isOwnerAlive = null)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var found = new List<RecoverableProject>(FindUntitled(autosaveDirectory, isOwnerAlive));
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var project in projects.Distinct(comparer))
        {
            if (ForProject(project, isOwnerAlive) is { } candidate && !found.Any(f => comparer.Equals(f.AutosavePath, candidate.AutosavePath)))
            {
                found.Add(candidate);
            }
        }

        return [.. found.OrderByDescending(f => f.SavedUtc)];
    }
}
