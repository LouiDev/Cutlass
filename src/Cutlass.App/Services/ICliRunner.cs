namespace Cutlass.App.Services;

/// <summary>
/// Runs a small command line tool (an agent client's CLI, AGENT_INTEGRATION 9.3) and captures its output. This is not
/// for ffmpeg, which always goes through <c>ProcessRunner</c>. <see cref="CliProcess"/> is the real one; tests use a
/// fake so they never touch the user's tool configuration.
/// </summary>
public interface ICliRunner
{
    /// <summary>Runs the tool off the calling thread and waits for it to exit.</summary>
    /// <param name="command">The tool's name on PATH (a client profile's command) or a full path.</param>
    /// <param name="arguments">The arguments, passed one by one (no quoting needed).</param>
    /// <param name="workingDirectory">The working directory, or null for the current one.</param>
    /// <param name="timeout">Kill the tool after this long.</param>
    /// <param name="cancellationToken">Cancels the wait and kills the tool.</param>
    /// <returns>The exit code and the captured output; <see cref="CliResult.NotFound"/> when the tool is missing.</returns>
    Task<CliResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
}
