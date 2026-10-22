namespace Cutlass.Media.Cache;

/// <summary>
/// The <c>manifest.json</c> of one media cache folder (TECH_BRIEF 6.1):
/// <c>{ fingerprint, ffmpegBuild, artifacts:{probe:1,...}, proxyMapping, lastAccessUtc }</c>. Treat an instance as a
/// value: <see cref="MediaCache"/> changes manifests with <c>with</c> and never mutates one it handed out.
/// </summary>
/// <remarks>
/// The properties are settable, not init-only, because the System.Text.Json source generator assigns every
/// init-only property in the object initializer and so overwrites the defaults of fields missing from the file.
/// </remarks>
public sealed record CacheManifest
{
    /// <summary>The media fingerprint (<c>sha256:&lt;hex&gt;</c>).</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>The ffmpeg build id that last wrote an artifact, for diagnostics.</summary>
    public string? FfmpegBuild { get; set; }

    /// <summary>The artifacts present, each with the version it was written with.</summary>
    public IReadOnlyDictionary<string, int> Artifacts { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>How the proxy maps onto the source, or null when no proxy was built.</summary>
    public ProxyMapping? ProxyMapping { get; set; }

    /// <summary>The last time an artifact was used, for the LRU (UTC).</summary>
    public DateTime LastAccessUtc { get; set; }

    /// <summary>True when the manifest records <paramref name="artifact"/> at its current version.</summary>
    /// <param name="artifact">The artifact name (<see cref="CacheArtifacts"/>).</param>
    /// <returns>True when current.</returns>
    public bool HasCurrent(string artifact) =>
        Artifacts.TryGetValue(artifact, out var version) && version == CacheArtifacts.VersionOf(artifact);

    /// <summary>A copy that records <paramref name="artifact"/> at its current version.</summary>
    /// <param name="artifact">The artifact name.</param>
    /// <returns>The new manifest.</returns>
    public CacheManifest WithArtifact(string artifact)
    {
        var artifacts = new Dictionary<string, int>(Artifacts, StringComparer.Ordinal)
        {
            [artifact] = CacheArtifacts.VersionOf(artifact),
        };
        return this with { Artifacts = artifacts };
    }

    /// <summary>A copy without <paramref name="artifact"/>.</summary>
    /// <param name="artifact">The artifact name.</param>
    /// <returns>The new manifest.</returns>
    public CacheManifest WithoutArtifact(string artifact)
    {
        var artifacts = new Dictionary<string, int>(Artifacts, StringComparer.Ordinal);
        artifacts.Remove(artifact);
        return this with { Artifacts = artifacts };
    }
}
