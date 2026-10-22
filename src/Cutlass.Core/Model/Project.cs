using System.Collections.Immutable;

namespace Cutlass.Core.Model;

/// <summary>
/// The root of the immutable document: the imported media and the single sequence. Every edit produces a new
/// <see cref="Project"/>; an edit that changes nothing returns the same reference.
/// </summary>
/// <param name="SchemaVersion">The file schema version (see <see cref="CurrentSchemaVersion"/>).</param>
/// <param name="Media">Every imported media asset.</param>
/// <param name="Sequence">The sequence (timeline).</param>
public sealed record Project(int SchemaVersion, ImmutableArray<MediaAsset> Media, Sequence Sequence)
{
    /// <summary>The schema version written by this build.</summary>
    public const int CurrentSchemaVersion = 1;
}
