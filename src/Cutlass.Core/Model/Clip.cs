namespace Cutlass.Core.Model;

/// <summary>
/// Base of every timeline item. Positions are integer frames; the clip covers <c>[Start, End)</c>.
/// </summary>
/// <param name="Id">Clip id (prefix <c>c_</c>).</param>
/// <param name="Start">First timeline frame.</param>
/// <param name="Duration">Length in frames, at least 1.</param>
/// <param name="Enabled">Disabled clips are drawn dimmed and not rendered.</param>
/// <param name="LinkId">Link group id (prefix <c>l_</c>), or null. A link is only a group id: linked clips may differ in start and duration.</param>
public abstract record Clip(string Id, long Start, long Duration, bool Enabled, string? LinkId)
{
    /// <summary>The first frame after the clip: <c>Start + Duration</c>.</summary>
    public long End => Start + Duration;
}
