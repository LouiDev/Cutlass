using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Cutlass.App.Services;

/// <summary>
/// The smoke run's log. <see cref="Write"/> never blocks: lines go through a channel to a background task that
/// prints them to stdout and appends them to the log file, so the UI thread does no file I/O.
/// </summary>
public sealed class SmokeLog : IAsyncDisposable
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Task _writer;

    /// <summary>Creates the log and starts its writer.</summary>
    /// <param name="path">The log file (replaced).</param>
    public SmokeLog(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = System.IO.Path.GetFullPath(path);
        _writer = Task.Run(WriteAllAsync);
    }

    /// <summary>The log file.</summary>
    public string Path { get; }

    /// <summary>Queues a line, prefixed with the seconds since the log started.</summary>
    /// <param name="message">The message.</param>
    public void Write(string message)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"[{_clock.Elapsed.TotalSeconds,7:0.000}] {message}");
        _lines.Writer.TryWrite(line);
    }

    /// <summary>Flushes every queued line and closes the file.</summary>
    /// <returns>A task that completes when the file is written.</returns>
    public async ValueTask DisposeAsync()
    {
        _lines.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    private async Task WriteAllAsync()
    {
        StreamWriter? file = null;
        try
        {
            var folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            file = new StreamWriter(Path, append: false, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync("Smoke log " + Path + " cannot be written: " + ex.Message).ConfigureAwait(false);
        }

        try
        {
            await foreach (var line in _lines.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                await Console.Out.WriteLineAsync(line).ConfigureAwait(false);
                if (file is not null)
                {
                    await file.WriteLineAsync(line).ConfigureAwait(false);
                }
            }

            await Console.Out.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            if (file is not null)
            {
                await file.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
