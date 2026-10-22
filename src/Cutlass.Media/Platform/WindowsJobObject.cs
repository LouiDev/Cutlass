using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Cutlass.Media.Platform;

/// <summary>
/// A Windows Job Object created with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> (TECH_BRIEF 5.4). Every ffmpeg and
/// ffprobe child is assigned to it, and the handle is kept for the app's lifetime, so when Cutlass exits (even when it
/// crashes or is killed) Windows closes the handle and kills every child.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsJobObject : IDisposable
{
    private const int JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x2000;

    private readonly SafeFileHandle _handle;

    private WindowsJobObject(SafeFileHandle handle)
    {
        _handle = handle;
    }

    /// <summary>Creates an anonymous job that kills its processes when its last handle closes.</summary>
    /// <returns>The job.</returns>
    /// <exception cref="Win32Exception">When the job cannot be created or configured.</exception>
    public static unsafe WindowsJobObject Create()
    {
        var raw = CreateJobObjectW(IntPtr.Zero, null);
        if (raw == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateJobObjectW failed.");
        }

        var handle = new SafeFileHandle(raw, ownsHandle: true);
        var info = default(JobObjectExtendedLimitInformation);
        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
        if (!SetInformationJobObject(
                handle,
                JobObjectExtendedLimitInformationClass,
                &info,
                (uint)sizeof(JobObjectExtendedLimitInformation)))
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, "SetInformationJobObject failed.");
        }

        return new WindowsJobObject(handle);
    }

    /// <summary>Creates the job, or returns null when Windows refuses (the pid registry still kills children on exit).</summary>
    /// <returns>The job, or null.</returns>
    public static WindowsJobObject? TryCreate()
    {
        try
        {
            return Create();
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    /// <summary>Assigns a started process to the job.</summary>
    /// <param name="process">The process. It must still be running.</param>
    /// <returns>True when assigned; false when the process already exited or Windows refused.</returns>
    public bool TryAssign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return AssignProcessToJobObject(_handle, process.SafeHandle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Closes the job handle, which kills every process still assigned to it.</summary>
    public void Dispose() => _handle.Dispose();

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetInformationJobObject(SafeHandle job, int infoClass, void* info, uint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeHandle job, SafeHandle process);

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }
}
