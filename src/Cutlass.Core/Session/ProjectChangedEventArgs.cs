using Cutlass.Core.Model;

namespace Cutlass.Core.Session;

/// <summary>Raised by <see cref="ProjectSession.Changed"/>. View models compare the references they care about.</summary>
public sealed class ProjectChangedEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="old">The project before the change.</param>
    /// <param name="new">The project after the change.</param>
    /// <param name="kind">Why it changed.</param>
    public ProjectChangedEventArgs(Project old, Project @new, ChangeKind kind)
    {
        Old = old;
        New = @new;
        Kind = kind;
    }

    /// <summary>The project before the change.</summary>
    public Project Old { get; }

    /// <summary>The project after the change (the session's current project).</summary>
    public Project New { get; }

    /// <summary>Why it changed.</summary>
    public ChangeKind Kind { get; }
}
