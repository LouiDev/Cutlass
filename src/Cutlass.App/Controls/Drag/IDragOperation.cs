using Cutlass.Core.Model;

namespace Cutlass.App.Controls.Drag;

/// <summary>
/// One timeline drag (TECH_BRIEF 10.2). Edit drags run as a session gesture: Begin starts it, Update applies the
/// snapped delta to the gesture start, Commit records exactly one undo step and Cancel (Esc) restores the start. View
/// drags (scrub, marquee, track resize) change no project state.
/// </summary>
public interface IDragOperation
{
    /// <summary>The tooltip text, for example <c>+00:00:01:12</c> or <c>Dur 00:00:04:03</c>, or null.</summary>
    string? Tooltip { get; }

    /// <summary>True when the drag hit a limit (the tooltip is drawn in red).</summary>
    bool TooltipIsLimit { get; }

    /// <summary>What the viewer should show, or null.</summary>
    ViewerHint? Hint { get; }

    /// <summary>The engaged snap target, or null (the control draws the snap line).</summary>
    long? SnapFrame { get; }

    /// <summary>The project at the drag start, for the faint outline of the original position, or null.</summary>
    Project? OriginProject { get; }

    /// <summary>The clips whose original position is outlined.</summary>
    IReadOnlyCollection<string> OutlineClipIds { get; }

    /// <summary>Starts the drag (a session gesture unless it is a pure view drag).</summary>
    /// <param name="c">The drag context.</param>
    void Begin(DragContext c);

    /// <summary>Applies the current pointer state.</summary>
    /// <param name="c">The drag context.</param>
    void Update(DragContext c);

    /// <summary>Ends the drag and records it.</summary>
    void Commit();

    /// <summary>Cancels the drag and restores the state at its start.</summary>
    void Cancel();
}
