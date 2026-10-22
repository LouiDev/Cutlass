namespace Cutlass.Agent.Api.Dto;

/// <summary>An export job (AGENT_INTEGRATION 4.4).</summary>
public sealed class ExportDto
{
    /// <summary>The id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>queued</c>, <c>running</c>, <c>completed</c>, <c>failed</c> or <c>canceled</c>.</summary>
    public string State { get; set; } = "queued";

    /// <summary>Progress 0 to 1.</summary>
    public double Fraction { get; set; }

    /// <summary>Estimated seconds left, or null.</summary>
    public double? EtaSeconds { get; set; }

    /// <summary>Encoding speed (1 is real time), or null.</summary>
    public double? Speed { get; set; }

    /// <summary>The output file.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>The preset id.</summary>
    public string Preset { get; set; } = string.Empty;

    /// <summary>Compiler warnings.</summary>
    public List<string> Warnings { get; set; } = [];

    /// <summary>Why it failed, or null.</summary>
    public string? Error { get; set; }

    /// <summary>The last ffmpeg log lines (failures only).</summary>
    public List<string> LogTail { get; set; } = [];
}
