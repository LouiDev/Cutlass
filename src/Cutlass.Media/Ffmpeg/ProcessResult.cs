using System.Text;

namespace Cutlass.Media.Ffmpeg;

/// <summary>The outcome of <see cref="ProcessRunner.RunAsync"/>: a process that ran to completion.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="Stdout">Everything written to stdout, as raw bytes.</param>
/// <param name="StderrTail">The last stderr lines (up to 300), oldest first.</param>
/// <param name="DisplayCommand">The command line, for display only.</param>
public sealed record ProcessResult(int ExitCode, byte[] Stdout, IReadOnlyList<string> StderrTail, string DisplayCommand)
{
    /// <summary>The number of stderr lines an <see cref="FfmpegException"/> carries.</summary>
    public const int ExceptionTailLines = 30;

    /// <summary>Stdout decoded as UTF-8.</summary>
    public string StdoutText => Encoding.UTF8.GetString(Stdout);

    /// <summary>Throws when the process failed.</summary>
    /// <returns>This result, for chaining.</returns>
    /// <exception cref="FfmpegException">When <see cref="ExitCode"/> is not zero.</exception>
    public ProcessResult EnsureSuccess()
    {
        if (ExitCode != 0)
        {
            throw new FfmpegException(ExitCode, Tail(ExceptionTailLines), DisplayCommand);
        }

        return this;
    }

    /// <summary>The last <paramref name="lines"/> stderr lines.</summary>
    /// <param name="lines">How many lines.</param>
    /// <returns>The lines, oldest first.</returns>
    public IReadOnlyList<string> Tail(int lines) =>
        lines >= StderrTail.Count ? StderrTail : [.. StderrTail.Skip(StderrTail.Count - Math.Max(0, lines))];
}
