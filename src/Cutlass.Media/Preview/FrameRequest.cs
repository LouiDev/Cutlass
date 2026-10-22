using Cutlass.Core.Model;

namespace Cutlass.Media.Preview;

/// <summary>
/// One scrub or step request for the <see cref="FrameServer"/> (TECH_BRIEF 8.1). The server renders the timeline
/// window <c>[Frame, Frame + 1)</c> of the snapshot at <see cref="Size"/>.
/// </summary>
/// <param name="Snapshot">The immutable project to render. The worker never locks or mutates it.</param>
/// <param name="Frame">The timeline frame to show.</param>
/// <param name="Size">The output size (even dimensions).</param>
/// <param name="UseProxies">True to read video from proxies where one exists.</param>
/// <param name="RequestId">A caller-chosen id, increasing per request, so the viewer can tell the newest frame.</param>
public sealed record FrameRequest(Project Snapshot, long Frame, PreviewSize Size, bool UseProxies, long RequestId);
