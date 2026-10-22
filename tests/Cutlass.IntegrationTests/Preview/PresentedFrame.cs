// Not ".Preview": that namespace would hide TimelineKit.Preview(...) in the Export tests, which import it statically.
namespace Cutlass.IntegrationTests.PreviewTests;

/// <summary>A copy of a frame the frame server presented.</summary>
/// <param name="RequestId">The request id.</param>
/// <param name="TimelineFrame">The timeline frame.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
/// <param name="Stride">The bytes per row.</param>
/// <param name="Pooled">Whether the original buffer was pooled.</param>
/// <param name="Pixels">The copied pixels.</param>
internal sealed record PresentedFrame(long RequestId, long TimelineFrame, int Width, int Height, int Stride, bool Pooled, byte[] Pixels);
