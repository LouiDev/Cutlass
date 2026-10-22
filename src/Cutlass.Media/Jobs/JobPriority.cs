namespace Cutlass.Media.Jobs;

/// <summary>How urgent a job is (TECH_BRIEF 6.7). Lower values run first.</summary>
public enum JobPriority
{
    /// <summary>Media with clips on screen or selected in the bin.</summary>
    Visible = 0,

    /// <summary>Everything else.</summary>
    Background = 1,
}
