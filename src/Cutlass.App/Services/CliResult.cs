namespace Cutlass.App.Services;

/// <summary>The outcome of a command line tool run through <see cref="ICliRunner"/>.</summary>
/// <param name="ExitCode">The exit code, or -1 when the tool did not start or timed out.</param>
/// <param name="StandardOutput">Everything the tool wrote to stdout.</param>
/// <param name="StandardError">Everything the tool wrote to stderr.</param>
/// <param name="NotFound">True when the tool is not installed (not on PATH); nothing ran.</param>
/// <param name="TimedOut">True when the tool was killed after the timeout.</param>
public sealed record CliResult(int ExitCode, string StandardOutput, string StandardError, bool NotFound = false, bool TimedOut = false)
{
    /// <summary>True when the tool ran and exited with 0.</summary>
    public bool Succeeded => !NotFound && !TimedOut && ExitCode == 0;

    /// <summary>stdout and stderr together, trimmed, for showing to the user.</summary>
    public string CombinedOutput =>
        string.Join('\n', new[] { StandardOutput.Trim(), StandardError.Trim() }.Where(t => t.Length > 0));

    /// <summary>The result of a tool that is not installed.</summary>
    /// <param name="message">Why it could not start.</param>
    /// <returns>The result.</returns>
    public static CliResult Missing(string message) => new(-1, string.Empty, message, NotFound: true);
}
