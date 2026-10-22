namespace Cutlass.Agent.Api.Dto;

/// <summary>The result of <c>GET /v1/media/{id}/peaks</c>: per bin minimum and maximum sample, -1 to 1.</summary>
public sealed class PeaksDto
{
    /// <summary>Bins per second of source.</summary>
    public double PeaksPerSecond { get; set; }

    /// <summary>The minimum per bin.</summary>
    public List<double> Min { get; set; } = [];

    /// <summary>The maximum per bin.</summary>
    public List<double> Max { get; set; } = [];
}
