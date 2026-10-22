namespace Cutlass.Agent.Api.Requests;

/// <summary>A range <c>[start, end)</c> in frames.</summary>
public sealed class FrameRange
{
    /// <summary>First frame.</summary>
    public long? Start { get; set; }

    /// <summary>End frame (exclusive).</summary>
    public long? End { get; set; }
}
