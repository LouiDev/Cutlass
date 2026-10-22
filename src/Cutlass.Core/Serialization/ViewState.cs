using System.Collections.Immutable;

namespace Cutlass.Core.Serialization;

/// <summary>
/// Per-project view state stored in the <c>&lt;name&gt;.cutlass.user</c> sidecar (DESIGN 11). Writing it never
/// dirties the project, and it is not undoable.
/// </summary>
/// <param name="PixelsPerFrame">Timeline zoom, 0.01 to 40.</param>
/// <param name="ScrollFrame">The first visible timeline frame.</param>
/// <param name="ScrollY">Vertical scroll of the track area in pixels.</param>
/// <param name="Playhead">The playhead frame.</param>
/// <param name="TrackHeights">Track heights in pixels (32 to 160) by track id; missing tracks use the defaults.</param>
/// <param name="PreviewQuality">Viewer quality: <c>auto</c>, <c>full</c>, <c>half</c> or <c>quarter</c>.</param>
/// <param name="UseProxies">True when the preview reads proxies.</param>
/// <param name="BinView">Media bin layout: <c>grid</c> or <c>list</c>.</param>
public sealed record ViewState(
    double PixelsPerFrame,
    long ScrollFrame,
    double ScrollY,
    long Playhead,
    ImmutableDictionary<string, double> TrackHeights,
    string PreviewQuality,
    bool UseProxies,
    string BinView)
{
    /// <summary>The smallest zoom in pixels per frame.</summary>
    public const double MinPixelsPerFrame = 0.01;

    /// <summary>The largest zoom in pixels per frame.</summary>
    public const double MaxPixelsPerFrame = 40;

    /// <summary>The smallest track height in pixels.</summary>
    public const double MinTrackHeight = 32;

    /// <summary>The largest track height in pixels.</summary>
    public const double MaxTrackHeight = 160;

    /// <summary>The valid <see cref="PreviewQuality"/> values.</summary>
    public static ImmutableArray<string> PreviewQualities { get; } = ["auto", "full", "half", "quarter"];

    /// <summary>The valid <see cref="BinView"/> values.</summary>
    public static ImmutableArray<string> BinViews { get; } = ["grid", "list"];

    /// <summary>The state of a project without a sidecar.</summary>
    public static ViewState Default { get; } = new(2, 0, 0, 0, ImmutableDictionary<string, double>.Empty, "auto", true, "grid");

    /// <summary>Clamps every value to its legal range and replaces unknown names with the defaults.</summary>
    /// <returns>The sanitized state.</returns>
    public ViewState Sanitize()
    {
        static double Clamp(double v, double min, double max, double fallback) => double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

        var heights = (TrackHeights ?? ImmutableDictionary<string, double>.Empty)
            .Where(kv => kv.Key is not null)
            .ToImmutableDictionary(kv => kv.Key, kv => Clamp(kv.Value, MinTrackHeight, MaxTrackHeight, 56), StringComparer.Ordinal);
        return new ViewState(
            Clamp(PixelsPerFrame, MinPixelsPerFrame, MaxPixelsPerFrame, Default.PixelsPerFrame),
            Math.Max(0, ScrollFrame),
            Clamp(ScrollY, 0, 1_000_000, 0),
            Math.Max(0, Playhead),
            heights,
            PreviewQualities.Contains(PreviewQuality) ? PreviewQuality : Default.PreviewQuality,
            UseProxies,
            BinViews.Contains(BinView) ? BinView : Default.BinView);
    }
}
