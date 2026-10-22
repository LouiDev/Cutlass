using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Export;

/// <summary>A <see cref="TheoryAttribute"/> that skips itself when ffmpeg is not installed, like <c>[FfmpegFact]</c>.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class FfmpegTheoryAttribute : TheoryAttribute
{
    /// <summary>Creates the attribute and sets <see cref="FactAttribute.Skip"/> when ffmpeg is missing.</summary>
    public FfmpegTheoryAttribute()
    {
        if (!TestFfmpeg.IsAvailable)
        {
            Skip = "ffmpeg and ffprobe were not found (set CUTLASS_FFMPEG or add them to PATH).";
        }
    }
}
