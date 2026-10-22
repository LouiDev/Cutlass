using System.Text.Json.Serialization;

namespace Cutlass.Media.Cache;

/// <summary>
/// Source-generated JSON metadata for the media cache files: <c>manifest.json</c>, <c>thumbs.json</c> and the
/// <c>fingerprints.json</c> memo. camelCase, indented, enums as strings, unknown fields ignored.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(CacheManifest))]
[JsonSerializable(typeof(ThumbnailIndexFile))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class CacheJsonContext : JsonSerializerContext;
