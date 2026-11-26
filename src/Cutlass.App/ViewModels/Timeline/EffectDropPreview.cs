namespace Cutlass.App.ViewModels.Timeline;

/// <summary>
/// What an Effects tab drop would change (the timeline's drop ghost): the cut a transition goes to (a span centred on
/// it), the span of a new title, or the clip a fade preset applies to.
/// </summary>
/// <param name="TrackId">The track.</param>
/// <param name="Start">The first frame of the span.</param>
/// <param name="Duration">The span length in frames.</param>
/// <param name="Category">Transition (a cut), Title (a new clip) or Adjustment (an existing clip).</param>
public sealed record EffectDropPreview(string TrackId, long Start, long Duration, EffectCategory Category);
