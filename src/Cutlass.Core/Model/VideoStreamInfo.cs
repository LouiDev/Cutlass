using Cutlass.Core.Time;

namespace Cutlass.Core.Model;

/// <summary>Probe results for the video stream of a media file.</summary>
/// <param name="StreamIndex">Absolute stream index in the container.</param>
/// <param name="Codec">Codec name, for example <c>hevc</c>.</param>
/// <param name="Width">Coded width in pixels.</param>
/// <param name="Height">Coded height in pixels.</param>
/// <param name="DisplayWidth">Width after rotation and SAR, as displayed.</param>
/// <param name="DisplayHeight">Height after rotation and SAR, as displayed.</param>
/// <param name="Rotation">Display rotation in degrees (0, 90, 180 or 270).</param>
/// <param name="Sar">Sample aspect ratio.</param>
/// <param name="RFrameRate">ffprobe's <c>r_frame_rate</c>.</param>
/// <param name="AvgFrameRate">ffprobe's <c>avg_frame_rate</c>.</param>
/// <param name="TimeBase">Stream time base.</param>
/// <param name="StartPts">Stream start timestamp in <paramref name="TimeBase"/> units.</param>
/// <param name="PixFmt">Pixel format, for example <c>yuv420p10le</c>.</param>
/// <param name="IsVfr">True when the stream has a variable frame rate.</param>
/// <param name="IsHdr">True for PQ or HLG transfer (shown without tone mapping).</param>
/// <param name="BitRate">Bit rate in bits per second, or 0 when unknown.</param>
public sealed record VideoStreamInfo(
    int StreamIndex,
    string Codec,
    int Width,
    int Height,
    int DisplayWidth,
    int DisplayHeight,
    int Rotation,
    Rational Sar,
    Rational RFrameRate,
    Rational AvgFrameRate,
    Rational TimeBase,
    long StartPts,
    string PixFmt,
    bool IsVfr,
    bool IsHdr,
    long BitRate);
