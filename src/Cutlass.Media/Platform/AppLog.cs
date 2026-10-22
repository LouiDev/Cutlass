using System.Globalization;
using System.Text;

namespace Cutlass.Media.Platform;

/// <summary>
/// A small, bounded diagnostics log in <see cref="AppPaths.LogsDirectory"/>: one file per UTC day,
/// <c>cutlass-yyyy-MM-dd.log</c>, at most <see cref="MaxFileBytes"/> each, and only the newest <see cref="MaxFiles"/>
/// files are kept. It records crashes (unhandled exceptions) and other faults nobody sees otherwise. Every member is
/// thread-safe and never throws: a log that cannot be written must not make a crash worse.
/// </summary>
public sealed class AppLog
{
    /// <summary>The size limit of one day's file; later lines that day are dropped.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>How many log files are kept; older ones are deleted when a new day's file starts.</summary>
    public const int MaxFiles = 14;

    /// <summary>The file name prefix.</summary>
    public const string FilePrefix = "cutlass-";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Room kept at the end of a file for the "limit reached" marker.
    private const int MarkerReserve = 256;

    private readonly object _gate = new();
    private readonly Func<DateTime> _utcNow;
    private string? _prunedFor;
    private string? _fullFile;

    /// <summary>Creates the log. Nothing is written until <see cref="Write"/>.</summary>
    /// <param name="directory">The logs folder (<see cref="AppPaths.LogsDirectory"/>).</param>
    /// <param name="utcNow">The clock (tests), or null for <see cref="DateTime.UtcNow"/>.</param>
    public AppLog(string directory, Func<DateTime>? utcNow = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = Path.GetFullPath(directory);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>The logs folder.</summary>
    public string Directory { get; }

    /// <summary>The file today's lines go to.</summary>
    public string CurrentFile => FileFor(_utcNow());

    /// <summary>The file of a given day.</summary>
    /// <param name="utc">A time on that day (UTC).</param>
    /// <returns>The full path.</returns>
    public string FileFor(DateTime utc) =>
        Path.Combine(Directory, FilePrefix + utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

    /// <summary>
    /// Appends one entry, prefixed with the UTC time. Multi-line text is kept as it is. When the day's file has
    /// reached <see cref="MaxFileBytes"/> the entry is dropped (a final marker line says so once).
    /// </summary>
    /// <param name="message">The text.</param>
    /// <returns>True when the entry was written.</returns>
    public bool Write(string message)
    {
        try
        {
            lock (_gate)
            {
                var now = _utcNow();
                var file = FileFor(now);
                System.IO.Directory.CreateDirectory(Directory);
                PruneOnce(file);
                var length = File.Exists(file) ? new FileInfo(file).Length : 0;
                if (string.Equals(_fullFile, file, StringComparison.Ordinal) || length >= MaxFileBytes - MarkerReserve)
                {
                    return false;
                }

                var stamp = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                var bytes = Utf8NoBom.GetBytes(stamp + " " + (message ?? string.Empty) + "\n");
                var written = true;
                if (length + bytes.Length > MaxFileBytes - MarkerReserve)
                {
                    // The reserve keeps room for this one marker, so the file never exceeds the limit.
                    bytes = Utf8NoBom.GetBytes(stamp + " (log size limit reached; further entries today are dropped)\n");
                    _fullFile = file;
                    written = false;
                }

                using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
                return written;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Appends an exception with its type, message and stack trace.</summary>
    /// <param name="context">What was happening, for example "Unhandled exception".</param>
    /// <param name="exception">The exception, or null.</param>
    /// <returns>True when the entry was written.</returns>
    public bool WriteException(string context, Exception? exception) =>
        Write(context + ": " + (exception?.ToString() ?? "(no exception object)"));

    private void PruneOnce(string currentFile)
    {
        if (string.Equals(_prunedFor, currentFile, StringComparison.Ordinal))
        {
            return;
        }

        _prunedFor = currentFile;
        try
        {
            // The names sort by date, so the oldest come first.
            var files = System.IO.Directory.GetFiles(Directory, FilePrefix + "*.log").Order(StringComparer.Ordinal).ToList();
            var excess = files.Count - (MaxFiles - 1);
            foreach (var old in files.Where(f => !string.Equals(f, currentFile, StringComparison.Ordinal)).Take(Math.Max(0, excess)))
            {
                File.Delete(old);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruning is housekeeping; the next day tries again.
        }
    }
}
