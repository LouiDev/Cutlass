namespace Cutlass.Core.Model;

/// <summary>An imported media file.</summary>
/// <param name="Id">Media id (prefix <c>m_</c>).</param>
/// <param name="Path">Absolute path, forward slashes in the project file.</param>
/// <param name="RelativePath">Path relative to the project file, or null when not saved yet or on another drive.</param>
/// <param name="Fingerprint">Content fingerprint, for example <c>sha256:...</c>.</param>
/// <param name="SizeBytes">File size in bytes at import.</param>
/// <param name="MtimeUtc">Last write time (UTC) at import.</param>
/// <param name="Info">Probe results.</param>
public sealed record MediaAsset(
    string Id,
    string Path,
    string? RelativePath,
    string Fingerprint,
    long SizeBytes,
    DateTime MtimeUtc,
    MediaInfo Info);
