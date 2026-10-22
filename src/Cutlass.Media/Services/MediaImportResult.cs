using Cutlass.Core.Model;

namespace Cutlass.Media.Services;

/// <summary>The outcome of importing one file with <see cref="MediaImporter"/>.</summary>
/// <param name="Asset">The new asset, already registered in the <see cref="MediaCatalog"/>.</param>
/// <param name="PosterPath">The poster JPEG, or null for audio-only media or when the poster failed.</param>
/// <param name="Warning">Something the user should know (no poster, no frame index), or null.</param>
public sealed record MediaImportResult(MediaAsset Asset, string? PosterPath, string? Warning);
