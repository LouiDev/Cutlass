namespace Cutlass.Agent.Api.Dto.Project;

/// <summary>The <c>sequence</c> part of the project summary.</summary>
public sealed class SequenceSummaryDto
{
    /// <summary>Canvas width.</summary>
    public int Width { get; set; }

    /// <summary>Canvas height.</summary>
    public int Height { get; set; }

    /// <summary>The rate as a ratio, for example <c>30000/1001</c>.</summary>
    public string FrameRate { get; set; } = "30/1";

    /// <summary>The rate as a number, rounded to 3 decimals.</summary>
    public double Fps { get; set; }

    /// <summary>The audio rate (48000).</summary>
    public int SampleRate { get; set; } = 48000;

    /// <summary>The end of the last clip in frames.</summary>
    public long DurationFrames { get; set; }

    /// <summary>The In point, or null.</summary>
    public long? InFrame { get; set; }

    /// <summary>The Out point, or null.</summary>
    public long? OutFrame { get; set; }

    /// <summary>The user's playhead.</summary>
    public long Playhead { get; set; }
}
