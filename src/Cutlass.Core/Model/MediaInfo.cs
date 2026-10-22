namespace Cutlass.Core.Model;

/// <summary>What ffprobe reported about a media file.</summary>
/// <param name="DurationMicros">Container duration in microseconds.</param>
/// <param name="FormatStartMicros">The container's <c>format.start_time</c> in microseconds (source time zero).</param>
/// <param name="FormatName">ffprobe's <c>format_name</c>, for example <c>mov,mp4,m4a,3gp,3g2,mj2</c>.</param>
/// <param name="IsStill">True for single images.</param>
/// <param name="Video">The chosen video stream, or null.</param>
/// <param name="Audio">The chosen audio stream, or null.</param>
public sealed record MediaInfo(
    long DurationMicros,
    long FormatStartMicros,
    string FormatName,
    bool IsStill,
    VideoStreamInfo? Video,
    AudioStreamInfo? Audio);
