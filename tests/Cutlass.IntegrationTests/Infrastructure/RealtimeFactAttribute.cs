using Xunit;

namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// A fact that plays media in real time and judges dropped frames and A/V lag. It needs ffmpeg and a machine that can
/// decode in real time; shared CI runners cannot be relied on for that, so <c>CUTLASS_SKIP_REALTIME=1</c> skips it
/// (the CI workflow sets it on the Windows leg, see TECH_BRIEF 11.3).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RealtimeFactAttribute : FactAttribute
{
    /// <summary>The environment variable that skips real-time tests.</summary>
    public const string SkipVariable = "CUTLASS_SKIP_REALTIME";

    /// <summary>Creates the attribute and decides whether the test is skipped.</summary>
    public RealtimeFactAttribute()
    {
        Skip = SkipReason();
    }

    /// <summary>The skip reason for real-time tests on this machine, or null to run them.</summary>
    /// <returns>The reason, or null.</returns>
    internal static string? SkipReason()
    {
        if (!TestFfmpeg.IsAvailable)
        {
            return "ffmpeg and ffprobe were not found (set CUTLASS_FFMPEG or add them to PATH).";
        }

        var value = Environment.GetEnvironmentVariable(SkipVariable);
        return string.IsNullOrEmpty(value) || value == "0"
            ? null
            : "Real-time playback tests are skipped on this machine (" + SkipVariable + " is set).";
    }
}
