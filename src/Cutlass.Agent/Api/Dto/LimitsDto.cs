namespace Cutlass.Agent.Api.Dto;

/// <summary>The request limits of AGENT_INTEGRATION 4.6.</summary>
public sealed class LimitsDto
{
    /// <summary>The largest request body in bytes.</summary>
    public int MaxBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>The longest id array.</summary>
    public int MaxIds { get; set; } = 1000;

    /// <summary>The most ranges per remove-ranges call.</summary>
    public int MaxRanges { get; set; } = 500;

    /// <summary>The widest frame render.</summary>
    public int MaxFrameWidth { get; set; } = 1920;

    /// <summary>The most contact sheet cells.</summary>
    public int MaxContactSheetCells { get; set; } = 64;

    /// <summary>The largest contact sheet in pixels.</summary>
    public long MaxContactSheetPixels { get; set; } = 16_000_000;

    /// <summary>The longest levels range in seconds.</summary>
    public int MaxLevelsSeconds { get; set; } = 1800;

    /// <summary>The most level intervals per call.</summary>
    public int MaxLevelIntervals { get; set; } = 100_000;

    /// <summary>The most peak bins.</summary>
    public int MaxPeakBins { get; set; } = 4000;

    /// <summary>The most files per import.</summary>
    public int MaxImportPaths { get; set; } = 32;

    /// <summary>Requests per second of the token bucket.</summary>
    public int RequestsPerSecond { get; set; } = 30;

    /// <summary>The token bucket burst.</summary>
    public int Burst { get; set; } = 60;
}
