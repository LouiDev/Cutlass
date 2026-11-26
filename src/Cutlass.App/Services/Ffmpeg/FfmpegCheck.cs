namespace Cutlass.App.Services.Ffmpeg;

/// <summary>The result of checking an ffmpeg location in Settings ("Validate", Browse, "Use PATH").</summary>
/// <param name="IsUsable">True when ffmpeg ran, ffprobe sits next to it and the build can render.</param>
/// <param name="StatusText">One line for the dialog, for example "found: N-126308, libavfilter 12" or the error.</param>
/// <param name="FfmpegPath">The ffmpeg that was checked, or null when none was found.</param>
public sealed record FfmpegCheck(bool IsUsable, string StatusText, string? FfmpegPath);
