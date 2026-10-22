using Cutlass.IntegrationTests.Infrastructure;

namespace Cutlass.IntegrationTests.Examples;

/// <summary>A fact that needs ffmpeg and PowerShell (Windows PowerShell or <c>pwsh</c>); skipped without either.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class PowerShellFactAttribute : FactAttribute
{
    /// <summary>Creates the attribute and sets <see cref="FactAttribute.Skip"/> when ffmpeg or PowerShell is missing.</summary>
    public PowerShellFactAttribute()
    {
        if (!TestFfmpeg.IsAvailable)
        {
            Skip = "ffmpeg and ffprobe were not found (set CUTLASS_FFMPEG or add them to PATH).";
        }
        else if (ExampleScripts.PowerShell is null)
        {
            Skip = "Neither Windows PowerShell nor pwsh was found.";
        }
    }
}
