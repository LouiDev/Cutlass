using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Cutlass.Media.Platform;

/// <summary>
/// Tracks every running ffmpeg and ffprobe child (TECH_BRIEF 5.4). It kills them all on shutdown
/// (<see cref="AppDomain.ProcessExit"/> and unhandled exceptions) and, where Job Objects do not exist (Linux), persists
/// them at most twice a second to this instance's own file, <c>pids-&lt;ownerPid&gt;-&lt;ownerStartTicks&gt;.json</c>
/// in the record directory, so a later start can kill orphans left by a crash. Several instances share the directory:
/// each writes only its own file, and a startup sweep only touches files whose owner is gone or that were written
/// before the last reboot.
/// </summary>
public sealed class ChildProcessRegistry : IDisposable
{
    /// <summary>How close a live process's start time must be to the recorded one to count as the same process.</summary>
    public static readonly TimeSpan StartTimeTolerance = TimeSpan.FromMilliseconds(20);

    /// <summary>The minimum interval between two writes of the record file.</summary>
    public static readonly TimeSpan MinWriteInterval = TimeSpan.FromMilliseconds(500);

    private const string FilePrefix = "pids-";
    private const string LegacyFileName = "pids.json";

    private readonly object _gate = new();
    private readonly object _fileGate = new();
    private readonly Dictionary<int, (Process Process, ChildProcessRecord Record)> _children = [];
    private readonly Timer _writeTimer;
    private readonly int _ownerPid;
    private readonly long _ownerStartTicks;
    private readonly string? _bootId;
    private DateTime _lastWriteUtc = DateTime.MinValue;
    private long _version;
    private long _writtenVersion = -1;
    private bool _writeScheduled;
    private bool _hooksInstalled;
    private bool _disposed;

    /// <summary>Creates a registry.</summary>
    /// <param name="recordDirectory">The folder holding the per-instance record files (the cache root), or null to
    /// keep the children in memory only (Windows, where the Job Object already guarantees cleanup).</param>
    public ChildProcessRegistry(string? recordDirectory)
    {
        _ownerPid = Environment.ProcessId;
        using (var self = Process.GetCurrentProcess())
        {
            _ownerStartTicks = StartTicksOf(self);
        }

        _bootId = CurrentBootId();
        RecordDirectory = recordDirectory is null ? null : Path.GetFullPath(recordDirectory);
        RecordFile = RecordDirectory is null ? null : Path.Combine(RecordDirectory, FileNameFor(_ownerPid, _ownerStartTicks));
        _writeTimer = new Timer(_ => WriteScheduled(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The folder of the record files, or null when nothing is persisted.</summary>
    public string? RecordDirectory { get; }

    /// <summary>This instance's record file, or null when nothing is persisted. It exists only while children run.</summary>
    public string? RecordFile { get; }

    /// <summary>The number of children currently registered.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _children.Count;
            }
        }
    }

    /// <summary>Creates the registry for this OS: persisted in <see cref="AppPaths.CacheRoot"/> except on Windows.</summary>
    /// <param name="paths">The app paths.</param>
    /// <returns>The registry.</returns>
    public static ChildProcessRegistry ForCurrentPlatform(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return new ChildProcessRegistry(OperatingSystem.IsWindows() ? null : paths.CacheRoot);
    }

    /// <summary>The record file name of an instance.</summary>
    /// <param name="ownerPid">The instance's pid.</param>
    /// <param name="ownerStartTicks">The instance's start ticks.</param>
    /// <returns><c>pids-&lt;ownerPid&gt;-&lt;ownerStartTicks&gt;.json</c>.</returns>
    public static string FileNameFor(int ownerPid, long ownerStartTicks) =>
        string.Create(CultureInfo.InvariantCulture, $"{FilePrefix}{ownerPid}-{ownerStartTicks}.json");

    /// <summary>The Linux boot id (<c>/proc/sys/kernel/random/boot_id</c>), or null elsewhere or when unreadable.</summary>
    /// <returns>The boot id.</returns>
    public static string? CurrentBootId()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        try
        {
            var text = File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim();
            return text.Length > 0 ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Decides what a startup sweep does with a record file: keep it when it is this instance's own file or while its
    /// owner runs (the same pid with a start time within <see cref="StartTimeTolerance"/>); delete it without killing
    /// anything when it was written in another boot (the boot ids differ, so its pids and start times mean nothing);
    /// otherwise kill the owner's surviving children and delete it.
    /// </summary>
    /// <param name="file">The file's content.</param>
    /// <param name="selfPid">This instance's pid.</param>
    /// <param name="selfStartTicks">This instance's start ticks.</param>
    /// <param name="currentBootId">The current boot id, or null where there is none.</param>
    /// <param name="lookup">Returns a live process's start ticks and name for a pid, or null when it does not exist.</param>
    /// <returns>The action.</returns>
    public static OrphanSweepAction Decide(
        ChildProcessFile file,
        int selfPid,
        long selfStartTicks,
        string? currentBootId,
        Func<int, (long StartTimeUtcTicks, string ProcessName)?> lookup)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(lookup);
        if (file.OwnerPid == selfPid && Math.Abs(file.OwnerStartTicks - selfStartTicks) <= StartTimeTolerance.Ticks)
        {
            return OrphanSweepAction.Keep;
        }

        if (!string.Equals(file.BootId, currentBootId, StringComparison.Ordinal))
        {
            return OrphanSweepAction.DeleteOnly;
        }

        var owner = file.OwnerPid > 0 ? lookup(file.OwnerPid) : null;
        if (owner is { } live && Math.Abs(live.StartTimeUtcTicks - file.OwnerStartTicks) <= StartTimeTolerance.Ticks)
        {
            return OrphanSweepAction.Keep;
        }

        return OrphanSweepAction.KillAndDelete;
    }

    /// <summary>
    /// Decides which recorded processes are orphans to kill: a live process with the recorded pid whose start time
    /// matches within <see cref="StartTimeTolerance"/> and whose name starts with <c>ffmpeg</c> or <c>ffprobe</c>.
    /// </summary>
    /// <param name="recorded">The records read from a gone instance's file.</param>
    /// <param name="lookup">Returns the live process's start time (UTC ticks) and name for a pid, or null when no
    /// such process exists.</param>
    /// <returns>The records whose processes should be killed.</returns>
    public static IReadOnlyList<ChildProcessRecord> SelectOrphans(
        IEnumerable<ChildProcessRecord> recorded,
        Func<int, (long StartTimeUtcTicks, string ProcessName)?> lookup)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        ArgumentNullException.ThrowIfNull(lookup);

        var result = new List<ChildProcessRecord>();
        foreach (var record in recorded)
        {
            if (record is null || record.Pid <= 0)
            {
                continue;
            }

            var live = lookup(record.Pid);
            if (live is null)
            {
                continue;
            }

            var (startTicks, name) = live.Value;
            var nameMatches = name.StartsWith("ffmpeg", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("ffprobe", StringComparison.OrdinalIgnoreCase);
            var timeMatches = Math.Abs(startTicks - record.StartTimeUtcTicks) <= StartTimeTolerance.Ticks;
            if (nameMatches && timeMatches)
            {
                result.Add(record);
            }
        }

        return result;
    }

    /// <summary>
    /// Sweeps the record directory at startup (<see cref="Decide"/>): kills the surviving children of instances that
    /// are gone and deletes their files, deletes unreadable files and files from another boot, and leaves live
    /// instances' files alone. A legacy shared <c>pids.json</c> names no owner, so it is deleted without killing
    /// anything. Does nothing when nothing is persisted.
    /// </summary>
    /// <returns>The number of processes killed.</returns>
    public int SweepOrphans()
    {
        if (RecordDirectory is null || !Directory.Exists(RecordDirectory))
        {
            return 0;
        }

        TryDelete(Path.Combine(RecordDirectory, LegacyFileName));
        string[] files;
        try
        {
            files = Directory.GetFiles(RecordDirectory, FilePrefix + "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var killed = 0;
        foreach (var path in files)
        {
            if (string.Equals(Path.GetFullPath(path), RecordFile, StringComparison.Ordinal))
            {
                continue;
            }

            var file = ReadFile(path);
            var action = file is null
                ? OrphanSweepAction.DeleteOnly
                : Decide(file, _ownerPid, _ownerStartTicks, _bootId, LookupLiveProcess);
            if (action == OrphanSweepAction.Keep)
            {
                continue;
            }

            if (action == OrphanSweepAction.KillAndDelete)
            {
                killed += Kill(SelectOrphans(file!.Children ?? [], LookupLiveProcess));
            }

            TryDelete(path);
        }

        return killed;
    }

    /// <summary>Registers a started child.</summary>
    /// <param name="process">The process, already started.</param>
    public void Register(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        long startTicks;
        string executable;
        try
        {
            startTicks = StartTicksOf(process);
            executable = process.StartInfo.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Already exited: nothing can be orphaned.
            return;
        }

        lock (_gate)
        {
            _children[process.Id] = (process, new ChildProcessRecord(process.Id, startTicks, executable));
            _version++;
            ScheduleWriteLocked();
        }
    }

    /// <summary>Removes a child after it exited.</summary>
    /// <param name="pid">The process id.</param>
    public void Unregister(int pid)
    {
        lock (_gate)
        {
            if (_children.Remove(pid))
            {
                _version++;
                ScheduleWriteLocked();
            }
        }
    }

    /// <summary>The records of the currently registered children.</summary>
    /// <returns>A snapshot ordered by pid.</returns>
    public IReadOnlyList<ChildProcessRecord> Snapshot()
    {
        lock (_gate)
        {
            return [.. _children.Values.Select(v => v.Record).OrderBy(r => r.Pid)];
        }
    }

    /// <summary>Kills every registered child (and its tree). Safe to call from shutdown handlers.</summary>
    public void KillAll()
    {
        List<Process> processes;
        lock (_gate)
        {
            processes = [.. _children.Values.Select(v => v.Process)];
        }

        foreach (var process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
            {
                // Already gone.
            }
        }
    }

    /// <summary>
    /// Kills every child in <see cref="AppDomain.ProcessExit"/> and <see cref="AppDomain.UnhandledException"/>.
    /// Calling it again does nothing.
    /// </summary>
    public void InstallShutdownHooks()
    {
        lock (_gate)
        {
            if (_hooksInstalled)
            {
                return;
            }

            _hooksInstalled = true;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => KillAll();
    }

    /// <summary>Writes this instance's record file now when anything is persisted (deleting it without children).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            _writeScheduled = false;
        }

        Write();
    }

    /// <summary>Stops the write timer and writes the final state. Does not kill children (see <see cref="KillAll"/>).</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _writeTimer.Dispose();
        Flush();
    }

    // Linux: the boot-relative start time from /proc (stable across sessions, see ProcStat); elsewhere the UTC start
    // time. A record file only ever meets processes of the same OS, so the two meanings never mix.
    private static long StartTicksOf(Process process) =>
        OperatingSystem.IsLinux() && ProcStat.Read(process.Id) is { } stat
            ? stat.StartTicks
            : process.StartTime.ToUniversalTime().Ticks;

    private static (long StartTimeUtcTicks, string ProcessName)? LookupLiveProcess(int pid)
    {
        if (OperatingSystem.IsLinux())
        {
            return ProcStat.Read(pid) is { } stat ? (stat.StartTicks, stat.Name) : null;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return (process.StartTime.ToUniversalTime().Ticks, process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    private static ChildProcessFile? ReadFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize(stream, MediaJsonContext.Default.ChildProcessFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static int Kill(IReadOnlyList<ChildProcessRecord> orphans)
    {
        var killed = 0;
        foreach (var orphan in orphans)
        {
            try
            {
                using var process = Process.GetProcessById(orphan.Pid);
                process.Kill(entireProcessTree: true);
                killed++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Exited between the lookup and the kill.
            }
        }

        return killed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind; a later sweep or write deals with it.
        }
    }

    private void ScheduleWriteLocked()
    {
        if (RecordFile is null || _writeScheduled || _disposed)
        {
            return;
        }

        _writeScheduled = true;
        var due = _lastWriteUtc + MinWriteInterval - DateTime.UtcNow;
        _writeTimer.Change(due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
    }

    private void WriteScheduled()
    {
        lock (_gate)
        {
            if (!_writeScheduled)
            {
                return;
            }

            _writeScheduled = false;
        }

        Write();
    }

    // The snapshot is taken under the lock and written outside it, so Register and Unregister never wait for the disk.
    // The file gate orders writes, and an older snapshot never replaces a newer one. No fsync: after a power loss the
    // children are dead anyway.
    private void Write()
    {
        if (RecordFile is null)
        {
            return;
        }

        List<ChildProcessRecord> records;
        long version;
        lock (_gate)
        {
            _lastWriteUtc = DateTime.UtcNow;
            records = [.. _children.Values.Select(v => v.Record).OrderBy(r => r.Pid)];
            version = _version;
        }

        lock (_fileGate)
        {
            if (version <= _writtenVersion)
            {
                return;
            }

            _writtenVersion = version;
            if (records.Count == 0)
            {
                TryDelete(RecordFile);
                return;
            }

            var temp = RecordFile + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                Directory.CreateDirectory(RecordDirectory!);
                using (var stream = File.Create(temp))
                {
                    JsonSerializer.Serialize(
                        stream,
                        new ChildProcessFile(_ownerPid, _ownerStartTicks, _bootId, records),
                        MediaJsonContext.Default.ChildProcessFile);
                }

                File.Move(temp, RecordFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a missed write only weakens orphan cleanup after a crash.
                _writtenVersion = -1;
                TryDelete(temp);
            }
        }
    }
}
