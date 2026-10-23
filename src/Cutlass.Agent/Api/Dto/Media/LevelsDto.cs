namespace Cutlass.Agent.Api.Dto.Media;

/// <summary>Loudness per interval in dBFS (floor -120): the result of the levels tools.</summary>
public sealed class LevelsDto
{
    /// <summary>The first frame (timeline levels) or 0.</summary>
    public long StartFrame { get; set; }

    /// <summary>The source start in flicks (media levels), or null.</summary>
    public long? StartFlicks { get; set; }

    /// <summary>The interval length in milliseconds.</summary>
    public int IntervalMs { get; set; }

    /// <summary>RMS per interval in dBFS.</summary>
    public List<double> RmsDb { get; set; } = [];

    /// <summary>Peak per interval in dBFS.</summary>
    public List<double> PeakDb { get; set; } = [];
}
