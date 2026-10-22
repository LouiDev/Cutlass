using Cutlass.Core.Model;

namespace Cutlass.Agent.Hosting;

/// <summary>The outcome of <see cref="IAgentHost.ImportAsync"/>.</summary>
/// <param name="Imported">The assets now in the bin, in request order (already present files are reported too).</param>
/// <param name="Failed">The files that could not be imported.</param>
/// <param name="Warnings">Notes such as a missing frame index.</param>
public sealed record HostImport(IReadOnlyList<MediaAsset> Imported, IReadOnlyList<HostImportFailure> Failed, IReadOnlyList<string> Warnings);
