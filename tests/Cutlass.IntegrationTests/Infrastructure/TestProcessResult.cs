using System.Text;

namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>The outcome of <see cref="TestProcess.RunAsync"/>.</summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StdoutBytes">Everything written to stdout, as raw bytes (rawvideo and PCM pipes are binary).</param>
/// <param name="Stderr">Everything written to stderr.</param>
public sealed record TestProcessResult(int ExitCode, byte[] StdoutBytes, string Stderr)
{
    /// <summary>Stdout decoded as UTF-8 text.</summary>
    public string Stdout => Encoding.UTF8.GetString(StdoutBytes);
}
