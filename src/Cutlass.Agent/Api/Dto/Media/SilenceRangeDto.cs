namespace Cutlass.Agent.Api.Dto.Media;

/// <summary>One silent range <c>[start, end)</c> in frames.</summary>
public sealed class SilenceRangeDto
{
    /// <summary>First frame.</summary>
    public long Start { get; set; }

    /// <summary>End frame (exclusive).</summary>
    public long End { get; set; }

    /// <summary>The length in seconds.</summary>
    public double Seconds { get; set; }
}
