using Cutlass.App.Services;

namespace Cutlass.Tests.App.Agent;

/// <summary>
/// A client CLI that never runs: it answers each call from a handler and records the arguments, so no test touches a
/// real client's configuration (AGENT_INTEGRATION 9.3).
/// </summary>
internal sealed class FakeCliRunner : ICliRunner
{
    private readonly Func<IReadOnlyList<string>, CliResult> _answer;

    /// <summary>Creates the fake.</summary>
    /// <param name="answer">Answers a call from its arguments.</param>
    public FakeCliRunner(Func<IReadOnlyList<string>, CliResult> answer)
    {
        _answer = answer;
    }

    /// <summary>Every call as "command arg arg ...".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>The working directories of the calls.</summary>
    public List<string?> Directories { get; } = [];

    /// <inheritdoc />
    public Task<CliResult> RunAsync(string command, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Calls.Add(command + " " + string.Join(' ', arguments));
        Directories.Add(workingDirectory);
        return Task.FromResult(_answer(arguments));
    }
}
