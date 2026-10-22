using System.Text.Json.Serialization;

namespace Cutlass.Media.Cache;

/// <summary>How a proxy's frames map onto its source's frames (TECH_BRIEF 6.6), stored in <c>manifest.json</c>.</summary>
public enum ProxyMapping
{
    /// <summary>Every proxy pts equals the source pts (each minus its file's format start).</summary>
    [JsonStringEnumMemberName("exact")]
    Exact = 0,

    /// <summary>Same frame count, different timestamps: seeks map by ordinal.</summary>
    [JsonStringEnumMemberName("ordinal")]
    Ordinal = 1,

    /// <summary>The frame count differs from the source; the original stays in use.</summary>
    [JsonStringEnumMemberName("rejected")]
    Rejected = 2,
}
