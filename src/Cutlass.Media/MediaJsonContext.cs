using System.Text.Json.Serialization;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;
using Cutlass.Media.Settings;

namespace Cutlass.Media;

/// <summary>
/// Source-generated JSON metadata for the small files the media layer owns: <c>settings.json</c>,
/// <c>capabilities.json</c> and the per-instance <c>pids-&lt;pid&gt;-&lt;start&gt;.json</c> files. camelCase, indented, enums as strings, unknown fields ignored.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(CapabilityCacheEntry))]
[JsonSerializable(typeof(List<ChildProcessRecord>))]
[JsonSerializable(typeof(ChildProcessFile))]
internal sealed partial class MediaJsonContext : JsonSerializerContext;
