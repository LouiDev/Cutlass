namespace Cutlass.Core.Model;

/// <summary>A generated text clip on a video track. Titles are never linked.</summary>
/// <param name="Id">Clip id (prefix <c>c_</c>).</param>
/// <param name="Start">First timeline frame.</param>
/// <param name="Duration">Length in frames, at least 1.</param>
/// <param name="Enabled">Disabled clips are not rendered.</param>
/// <param name="Title">Text and styling.</param>
/// <param name="Opacity">Opacity 0 to 1.</param>
/// <param name="FadeInFrames">Fade-in length in frames.</param>
/// <param name="FadeOutFrames">Fade-out length in frames.</param>
public sealed record TitleClip(
    string Id,
    long Start,
    long Duration,
    bool Enabled,
    TitleSpec Title,
    double Opacity,
    long FadeInFrames,
    long FadeOutFrames) : Clip(Id, Start, Duration, Enabled, null);
