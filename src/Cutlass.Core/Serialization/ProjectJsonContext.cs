using System.Text.Json;
using System.Text.Json.Serialization;
using Cutlass.Core.Serialization.Dto;

namespace Cutlass.Core.Serialization;

/// <summary>
/// The source-generated System.Text.Json context for project files and sidecars: camelCase, indented with two spaces
/// and <c>\n</c> line endings on every OS (so files are byte-identical across platforms), comments and trailing commas
/// tolerated on read, unknown fields ignored.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\n",
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ProjectFileDto))]
[JsonSerializable(typeof(ViewStateDto))]
internal sealed partial class ProjectJsonContext : JsonSerializerContext;
