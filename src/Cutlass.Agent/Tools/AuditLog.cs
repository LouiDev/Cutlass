using System.Globalization;
using Cutlass.Media.Platform;

namespace Cutlass.Agent.Tools;

/// <summary>
/// The audit log of agent calls (AGENT_INTEGRATION 4.6): a ring of the newest 500 entries for the Agent panel, and a
/// line per call in the app log with the prefix <c>agent:</c>. Tokens and bodies are never logged. Thread-safe;
/// <see cref="Recorded"/> fires on the recording thread.
/// </summary>
public sealed class AuditLog
{
    /// <summary>The ring size.</summary>
    public const int DefaultCapacity = 500;

    private readonly AppLog? _log;
    private readonly LinkedList<AuditEntry> _ring = new();
    private readonly object _gate = new();

    /// <summary>Creates the log.</summary>
    /// <param name="log">The app log to append to, or null.</param>
    /// <param name="capacity">The ring size.</param>
    public AuditLog(AppLog? log, int capacity = DefaultCapacity)
    {
        _log = log;
        Capacity = Math.Max(1, capacity);
    }

    /// <summary>Raised after each recorded entry, on the recording thread.</summary>
    public event Action<AuditEntry>? Recorded;

    /// <summary>The ring size.</summary>
    public int Capacity { get; }

    /// <summary>The entries, newest first.</summary>
    /// <returns>A copy.</returns>
    public IReadOnlyList<AuditEntry> Snapshot()
    {
        lock (_gate)
        {
            return [.. _ring];
        }
    }

    /// <summary>Records an entry.</summary>
    /// <param name="entry">The entry.</param>
    public void Record(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _ring.AddFirst(entry);
            while (_ring.Count > Capacity)
            {
                _ring.RemoveLast();
            }
        }

        _log?.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"agent: {entry.Agent} {entry.Tool} {entry.Code} {entry.DurationMs} ms rev {entry.Revision} {entry.Arguments}"));
        try
        {
            Recorded?.Invoke(entry);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // A failing listener must not fail the call it records.
        }
    }

    /// <summary>Records a lock transition as an entry with the tool name <c>lock</c>.</summary>
    /// <param name="agent">The agent.</param>
    /// <param name="what">What happened, for example <c>granted</c>.</param>
    /// <param name="revision">The revision.</param>
    public void RecordLock(string agent, string what, long revision) =>
        Record(new AuditEntry(DateTime.UtcNow, agent, "lock", 0, "ok", revision, what));

    /// <summary>Shortens a JSON argument text to one line of at most 200 characters.</summary>
    /// <param name="json">The arguments as JSON text.</param>
    /// <returns>The summary.</returns>
    public static string Summarize(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return string.Empty;
        }

        var line = json.Replace('\r', ' ').Replace('\n', ' ');
        return line.Length <= 200 ? line : line[..197] + "...";
    }
}
