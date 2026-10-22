namespace Cutlass.Core.Session;

/// <summary>
/// The undo and redo stacks with a capacity (500 by default; the oldest entry is dropped first) and coalescing.
/// Not thread safe: the session uses it on the UI thread only.
/// </summary>
public sealed class UndoHistory
{
    /// <summary>The default number of undo steps (DESIGN 2.1).</summary>
    public const int DefaultCapacity = 500;

    /// <summary>The default coalescing window in milliseconds (TECH_BRIEF 3.4).</summary>
    public const long DefaultCoalesceWindowMs = 1500;

    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];

    /// <summary>Creates an empty history.</summary>
    /// <param name="capacity">The maximum number of undo entries (at least 1).</param>
    public UndoHistory(int capacity = DefaultCapacity)
    {
        Capacity = Math.Max(1, capacity);
    }

    /// <summary>The maximum number of undo entries.</summary>
    public int Capacity { get; }

    /// <summary>The number of entries that can be undone.</summary>
    public int UndoCount => _undo.Count;

    /// <summary>The number of entries that can be redone.</summary>
    public int RedoCount => _redo.Count;

    /// <summary>The entry the next undo reverts, or null.</summary>
    public HistoryEntry? NextUndo => _undo.Count > 0 ? _undo[^1] : null;

    /// <summary>The entry the next redo re-applies, or null.</summary>
    public HistoryEntry? NextRedo => _redo.Count > 0 ? _redo[^1] : null;

    /// <summary>Records a new entry. Clears the redo stack and drops the oldest entry beyond the capacity.</summary>
    /// <param name="entry">The entry.</param>
    public void Push(HistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _redo.Clear();
        _undo.Add(entry);
        if (_undo.Count > Capacity)
        {
            _undo.RemoveRange(0, _undo.Count - Capacity);
        }
    }

    /// <summary>
    /// Merges <paramref name="entry"/> into the top entry when both carry the same non-null coalesce key, the redo
    /// stack is empty, and the top entry was recorded at most <paramref name="windowMs"/> before. The merged entry keeps
    /// the older <see cref="HistoryEntry.Before"/>; when it ends where it started, it is dropped altogether.
    /// </summary>
    /// <param name="entry">The new entry.</param>
    /// <param name="windowMs">The coalescing window in milliseconds.</param>
    /// <returns>True when merged (or dropped as a no-op); false when the caller should <see cref="Push"/> it.</returns>
    public bool TryCoalesce(HistoryEntry entry, long windowMs = DefaultCoalesceWindowMs)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.CoalesceKey is null || _redo.Count > 0 || _undo.Count == 0)
        {
            return false;
        }

        var top = _undo[^1];
        if (top.CoalesceKey != entry.CoalesceKey || entry.TimestampMs - top.TimestampMs > windowMs
            || entry.TimestampMs < top.TimestampMs || !ReferenceEquals(top.After, entry.Before))
        {
            return false;
        }

        if (ReferenceEquals(top.Before, entry.After))
        {
            _undo.RemoveAt(_undo.Count - 1);
        }
        else
        {
            _undo[^1] = entry with { Before = top.Before };
        }

        return true;
    }

    /// <summary>Moves the top undo entry to the redo stack.</summary>
    /// <returns>The entry to revert, or null when there is nothing to undo.</returns>
    public HistoryEntry? Undo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(entry);
        return entry;
    }

    /// <summary>Moves the top redo entry back to the undo stack.</summary>
    /// <returns>The entry to re-apply, or null when there is nothing to redo.</returns>
    public HistoryEntry? Redo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(entry);
        return entry;
    }

    /// <summary>Forgets every entry.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
