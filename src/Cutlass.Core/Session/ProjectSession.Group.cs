using Cutlass.Core.Model;

namespace Cutlass.Core.Session;

/// <summary>
/// Groups and the write gate (AGENT_INTEGRATION 2.6). A group collects every edit between <see cref="BeginGroup"/>
/// and <see cref="EndGroup"/> into one undo entry; the write gate is the backstop that keeps the user from editing
/// while an agent holds the project.
/// </summary>
public sealed partial class ProjectSession
{
    /// <summary>The warning of an edit the write gate refused.</summary>
    public const string RefusedMessage = "An agent holds the project.";

    private SessionWriter _writer = SessionWriter.User;
    private Project? _groupStart;
    private string? _groupLabel;

    /// <summary>
    /// Raised when the write gate refused a write, with the refused edit's label (or <c>Gesture</c>, <c>Undo</c>,
    /// <c>Redo</c>, <c>Replace</c>).
    /// </summary>
    public event EventHandler<string>? WriteRefused;

    /// <summary>
    /// Decides whether a writer may change the project. Null allows every writer. The agent lock installs one that
    /// refuses <see cref="SessionWriter.User"/> while an agent holds the project.
    /// </summary>
    public Func<SessionWriter, bool>? WriteGate { get; set; }

    /// <summary>The writer of the current scope: <see cref="SessionWriter.User"/> unless inside <see cref="WriteAs"/>.</summary>
    public SessionWriter Writer => _writer;

    /// <summary>True while a group is open.</summary>
    public bool InGroup => _groupStart is not null;

    /// <summary>The label of the open group, or null.</summary>
    public string? GroupLabel => _groupLabel;

    /// <summary>Runs the following session calls as <paramref name="writer"/> until the scope is disposed. UI thread only.</summary>
    /// <param name="writer">The writer.</param>
    /// <returns>The scope; dispose it to restore the previous writer.</returns>
    public IDisposable WriteAs(SessionWriter writer)
    {
        var previous = _writer;
        _writer = writer;
        return new WriterScope(this, previous);
    }

    /// <summary>
    /// Opens a group: until <see cref="EndGroup"/> or <see cref="CancelGroup"/>, edits are applied and raised as
    /// <see cref="ChangeKind.Edit"/> but not recorded. A running gesture is cancelled first, as <see cref="Execute"/>
    /// does. Does nothing (and raises <see cref="WriteRefused"/>) when the write gate refuses the current writer.
    /// </summary>
    /// <param name="label">The undo label of the group.</param>
    /// <exception cref="InvalidOperationException">When a group is already open (groups do not nest).</exception>
    public void BeginGroup(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (InGroup)
        {
            throw new InvalidOperationException("A group is already open; groups do not nest.");
        }

        if (!IsWriteAllowed())
        {
            WriteRefused?.Invoke(this, label);
            return;
        }

        if (InGesture)
        {
            CancelGesture();
        }

        _groupStart = Current;
        _groupLabel = label;
    }

    /// <summary>
    /// Closes the open group and records one entry from the group start to <see cref="Current"/>, raising
    /// <see cref="ChangeKind.GroupCommit"/>. Records and raises nothing when the group changed nothing.
    /// </summary>
    /// <returns>True when an entry was recorded.</returns>
    public bool EndGroup()
    {
        if (_groupStart is not { } start)
        {
            return false;
        }

        if (InGesture)
        {
            CancelGesture();
        }

        var label = _groupLabel ?? "Group";
        _groupStart = null;
        _groupLabel = null;
        if (ReferenceEquals(start, Current))
        {
            return false;
        }

        _history.Push(new HistoryEntry(start, Current, label, null, _clockMs()));
        Raise(start, ChangeKind.GroupCommit);
        return true;
    }

    /// <summary>
    /// Closes the open group and restores the group start, recording nothing. Raises
    /// <see cref="ChangeKind.GroupCancel"/> when the group had changed the project.
    /// </summary>
    public void CancelGroup()
    {
        if (_groupStart is not { } start)
        {
            return;
        }

        if (InGesture)
        {
            CancelGesture();
        }

        _groupStart = null;
        _groupLabel = null;
        if (ReferenceEquals(start, Current))
        {
            return;
        }

        var old = Current;
        Current = start;
        Raise(old, ChangeKind.GroupCancel);
    }

    private bool IsWriteAllowed() => WriteGate is not { } gate || gate(_writer);

    private sealed class WriterScope(ProjectSession session, SessionWriter previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            session._writer = previous;
        }
    }
}
