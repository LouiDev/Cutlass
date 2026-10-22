namespace Cutlass.Media.Preview;

/// <summary>
/// Receives rendered frames (TECH_BRIEF 8.1 and 10.3). Implemented in the App by <c>FramePresenter</c>.
/// </summary>
public interface IVideoFrameSink
{
    /// <summary>
    /// Takes a frame. Called on a worker thread, never the UI thread. The sink owns the frame from now on: it must
    /// call <see cref="VideoFrame.Return"/> once it has copied the pixels, and it must never write into
    /// <see cref="VideoFrame.Buffer"/> (unpooled buffers are shared with the frame cache).
    /// </summary>
    /// <param name="frame">The frame.</param>
    void Present(VideoFrame frame);
}
