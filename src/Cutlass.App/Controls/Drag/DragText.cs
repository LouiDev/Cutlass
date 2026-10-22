using Cutlass.Core.Time;

namespace Cutlass.App.Controls.Drag;

/// <summary>Tooltip text for drags (DESIGN 5.6).</summary>
public static class DragText
{
    /// <summary>A signed delta timecode, for example <c>+00:00:01:12</c> or <c>-00:00:00:03</c>.</summary>
    /// <param name="frames">The delta in frames.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The text.</returns>
    public static string Delta(long frames, FrameRate rate) => frames < 0 ? Timecode.Format(frames, rate) : "+" + Timecode.Format(frames, rate);

    /// <summary>A duration, for example <c>Dur 00:00:04:03</c>.</summary>
    /// <param name="frames">The duration in frames.</param>
    /// <param name="rate">The sequence rate.</param>
    /// <returns>The text.</returns>
    public static string Duration(long frames, FrameRate rate) => "Dur " + Timecode.Format(frames, rate);
}
