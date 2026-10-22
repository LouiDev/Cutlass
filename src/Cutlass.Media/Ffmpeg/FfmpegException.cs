namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// An ffmpeg or ffprobe process failed: it could not start, exited with a non-zero code, timed out, or produced
/// output Cutlass cannot use. Carries the exit code, the tail of stderr and the display command (TECH_BRIEF 9).
/// </summary>
public sealed class FfmpegException : Exception
{
    /// <summary>The exit code used when the process never produced one (it did not start or was killed on timeout).</summary>
    public const int NoExitCode = -1;

    /// <summary>Creates the exception for a process that exited with a non-zero code.</summary>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="stderrTail">The last stderr lines (normally 30).</param>
    /// <param name="displayCommand">The command line, for display only.</param>
    public FfmpegException(int exitCode, IReadOnlyList<string> stderrTail, string displayCommand)
        : this(DefaultMessage(exitCode, stderrTail, displayCommand), exitCode, stderrTail, displayCommand)
    {
    }

    /// <summary>Creates the exception with a custom message.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="exitCode">The exit code, or <see cref="NoExitCode"/>.</param>
    /// <param name="stderrTail">The last stderr lines.</param>
    /// <param name="displayCommand">The command line, for display only.</param>
    /// <param name="innerException">The underlying exception, if any.</param>
    public FfmpegException(
        string message,
        int exitCode,
        IReadOnlyList<string> stderrTail,
        string displayCommand,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        StderrTail = stderrTail ?? [];
        DisplayCommand = displayCommand ?? string.Empty;
    }

    /// <summary>The exit code, or <see cref="NoExitCode"/>.</summary>
    public int ExitCode { get; }

    /// <summary>The last stderr lines, oldest first.</summary>
    public IReadOnlyList<string> StderrTail { get; }

    /// <summary>The command line, for display only (never executed).</summary>
    public string DisplayCommand { get; }

    /// <summary>The stderr tail joined with newlines, for error dialogs and logs.</summary>
    public string StderrText => string.Join('\n', StderrTail);

    private static string DefaultMessage(int exitCode, IReadOnlyList<string> stderrTail, string displayCommand)
    {
        var tool = ToolName(displayCommand);
        var last = stderrTail?.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
        return last is null
            ? $"{tool} exited with code {exitCode}."
            : $"{tool} exited with code {exitCode}: {last.Trim()}";
    }

    private static string ToolName(string? displayCommand)
    {
        if (string.IsNullOrWhiteSpace(displayCommand))
        {
            return "ffmpeg";
        }

        var first = displayCommand.TrimStart();
        var quote = first[0] is '"' or '\'' ? first[0] : '\0';
        var end = quote == '\0' ? first.IndexOf(' ', StringComparison.Ordinal) : first.IndexOf(quote, 1);
        var exe = quote == '\0'
            ? (end < 0 ? first : first[..end])
            : (end < 0 ? first[1..] : first[1..end]);
        var name = Path.GetFileNameWithoutExtension(exe);
        return string.IsNullOrEmpty(name) ? "ffmpeg" : name;
    }
}
