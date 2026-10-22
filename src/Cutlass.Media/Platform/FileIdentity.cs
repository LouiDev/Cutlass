using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cutlass.Media.Platform;

/// <summary>
/// Which file a path names, independent of its spelling (TECH_BRIEF 9): the volume serial and file index on Windows,
/// <c>st_dev</c> and <c>st_ino</c> on Linux. Two paths name the same file when their identities are equal, which
/// catches case differences, short names, symbolic links and hard links that a string comparison misses. Also reports
/// the kind of a path (regular file, folder or something else such as a device or a FIFO) without opening it.
/// </summary>
/// <param name="Device">The volume serial number (Windows) or <c>st_dev</c> (Linux).</param>
/// <param name="FileIndex">The file index (Windows) or <c>st_ino</c> (Linux).</param>
public readonly partial record struct FileIdentity(ulong Device, ulong FileIndex)
{
    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint UnixTypeMask = 0xF000;
    private const uint UnixRegular = 0x8000;
    private const uint UnixDirectory = 0x4000;

    /// <summary>The identity of an existing file or folder, or null when it does not exist or cannot be read.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The identity, or null.</returns>
    public static FileIdentity? TryGet(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return WindowsIdentity(Path.GetFullPath(path));
            }

            return UnixStat(Path.GetFullPath(path)) is { } stat ? new FileIdentity(stat.Device, stat.Inode) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException
            or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// True when two paths name the same existing file. When either identity cannot be read, the full paths are
    /// compared instead (case-insensitively on Windows).
    /// </summary>
    /// <param name="a">One path.</param>
    /// <param name="b">The other path.</param>
    /// <returns>True for the same file.</returns>
    public static bool SameFile(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (TryGet(a) is { } ia && TryGet(b) is { } ib)
        {
            return ia == ib;
        }

        try
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// The kind of a path without opening it for reading: a regular file, a folder, something else (a device, a
    /// FIFO, a socket), or missing. Symbolic links are followed.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The kind.</returns>
    public static FileKind KindOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var full = Path.GetFullPath(path);
                if (full.StartsWith(@"\\.\", StringComparison.Ordinal))
                {
                    // \\.\COM1, \\.\pipe\x and friends.
                    return FileKind.Other;
                }

                if (!File.Exists(full))
                {
                    return Directory.Exists(full) ? FileKind.Directory : FileKind.Missing;
                }

                return (File.GetAttributes(full) & FileAttributes.Device) != 0 ? FileKind.Other : FileKind.Regular;
            }

            if (UnixStat(Path.GetFullPath(path)) is not { } stat)
            {
                return FileKind.Missing;
            }

            return (stat.Mode & UnixTypeMask) switch
            {
                UnixRegular => FileKind.Regular,
                UnixDirectory => FileKind.Directory,
                _ => FileKind.Other,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or DllNotFoundException or EntryPointNotFoundException)
        {
            return FileKind.Missing;
        }
    }

    private static FileIdentity? WindowsIdentity(string path)
    {
        // FILE_READ_ATTRIBUTES works on files other programs hold open exclusively; backup semantics allow folders.
        using var handle = CreateFileW(path, FileReadAttributes, FileShareAll, 0, OpenExisting, FileFlagBackupSemantics, 0);
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
        {
            return null;
        }

        return new FileIdentity(info.VolumeSerialNumber, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    // stat(2) through glibc: st_dev and st_ino lead the structure on x64 and arm64; st_mode follows at 24 on x64
    // (after the 8-byte st_nlink) and at 16 on arm64. glibc before 2.33 exports only __xstat.
    private static (ulong Device, ulong Inode, uint Mode)? UnixStat(string path)
    {
        var buffer = new byte[256];
        int result;
        try
        {
            result = Stat(path, buffer);
        }
        catch (EntryPointNotFoundException)
        {
            result = XStat(1, path, buffer);
        }

        if (result != 0)
        {
            return null;
        }

        var modeOffset = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 16 : 24;
        return (BitConverter.ToUInt64(buffer, 0), BitConverter.ToUInt64(buffer, 8), BitConverter.ToUInt32(buffer, modeOffset));
    }

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [LibraryImport("libc", EntryPoint = "stat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Stat(string path, byte[] buffer);

    [LibraryImport("libc", EntryPoint = "__xstat", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int XStat(int version, string path, byte[] buffer);

    // FILETIME is two DWORDs, so the structure packs on 4 bytes.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
