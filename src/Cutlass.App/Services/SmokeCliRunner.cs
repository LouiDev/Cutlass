namespace Cutlass.App.Services;

/// <summary>
/// The client CLI runner of a smoke run (AGENT_INTEGRATION 9.3): it never starts a process. Every call is logged and
/// answered "not found", so a smoke run can never add, check or remove a real client registration; the client
/// registry runs in a sandbox home at the same time.
/// </summary>
public sealed class SmokeCliRunner : ICliRunner
{
    private readonly Action<string>? _log;

    /// <summary>Creates the runner.</summary>
    /// <param name="log">Writes one line per call, or null.</param>
    public SmokeCliRunner(Action<string>? log)
    {
        _log = log;
    }

    /// <inheritdoc />
    public Task<CliResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(arguments);
        _log?.Invoke("smoke CLI (not run): " + command + " " + string.Join(' ', arguments));
        return Task.FromResult(CliResult.Missing(command + " is not run in a smoke run."));
    }
}
