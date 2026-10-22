namespace Cutlass.Core.Session;

/// <summary>Why <see cref="ProjectSession.Current"/> (or its saved state) changed.</summary>
public enum ChangeKind
{
    /// <summary>An <see cref="ProjectSession.Execute"/> call recorded or coalesced an edit.</summary>
    Edit,

    /// <summary>A live gesture update; the project changes without a history entry yet.</summary>
    GestureUpdate,

    /// <summary>A gesture was committed as one history entry.</summary>
    GestureCommit,

    /// <summary>A gesture was cancelled and the gesture start restored.</summary>
    GestureCancel,

    /// <summary>Undo.</summary>
    Undo,

    /// <summary>Redo.</summary>
    Redo,

    /// <summary>A different project was loaded and the history cleared.</summary>
    Replace,

    /// <summary>The current project was marked as saved (only <see cref="ProjectSession.IsDirty"/> changed).</summary>
    Saved,

    /// <summary>A group was committed as one history entry (treat like <see cref="GestureCommit"/>).</summary>
    GroupCommit,

    /// <summary>A group was cancelled and the group start restored (treat like <see cref="GestureCancel"/>).</summary>
    GroupCancel,
}
