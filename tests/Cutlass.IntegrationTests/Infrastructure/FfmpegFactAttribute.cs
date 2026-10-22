namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when ffmpeg is not installed. xunit 2 has no runtime skip, so the
/// decision is made in the constructor (TECH_BRIEF 11.2).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FfmpegFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute and sets <see cref="FactAttribute.Skip"/> when ffmpeg is missing.</summary>
    public FfmpegFactAttribute()
    {
        if (!TestFfmpeg.IsAvailable)
        {
            Skip = "ffmpeg and ffprobe were not found (set CUTLASS_FFMPEG or add them to PATH).";
        }
    }
}
