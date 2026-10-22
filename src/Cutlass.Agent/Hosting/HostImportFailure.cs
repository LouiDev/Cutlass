namespace Cutlass.Agent.Hosting;

/// <summary>One file an import could not add.</summary>
/// <param name="Path">The file.</param>
/// <param name="Message">Why, as a sentence.</param>
public sealed record HostImportFailure(string Path, string Message);
