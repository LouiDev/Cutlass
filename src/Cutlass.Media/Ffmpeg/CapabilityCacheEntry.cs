using Cutlass.Compiler.Capabilities;

namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// The JSON shape of <c>capabilities.json</c>: the probe results for one ffmpeg executable, keyed by its path, size
/// and modification time (TECH_BRIEF 5.2).
/// </summary>
internal sealed record CapabilityCacheEntry
{
    /// <summary>The cache format version; a different value is ignored.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The cache format version.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>The ffmpeg executable path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The executable size in bytes.</summary>
    public long Size { get; set; }

    /// <summary>The executable's last write time in UTC ticks.</summary>
    public long MtimeUtcTicks { get; set; }

    /// <summary>The build id.</summary>
    public string BuildId { get; set; } = string.Empty;

    /// <summary>The libavfilter major version.</summary>
    public int AvfilterMajor { get; set; }

    /// <summary>The libavfilter minor version.</summary>
    public int AvfilterMinor { get; set; }

    /// <summary>The detected script syntax.</summary>
    public FilterScriptSyntax ScriptSyntax { get; set; }

    /// <summary>The filter names.</summary>
    public List<string> Filters { get; set; } = [];

    /// <summary>The encoder names.</summary>
    public List<string> Encoders { get; set; } = [];
}
