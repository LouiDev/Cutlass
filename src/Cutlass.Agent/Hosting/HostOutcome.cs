using Cutlass.Agent.Api;

namespace Cutlass.Agent.Hosting;

/// <summary>The outcome of a host operation (new, open, save).</summary>
/// <param name="Error">The error, or null on success.</param>
/// <param name="Path">The project path afterwards (save: the saved path), or null.</param>
/// <param name="Warnings">Notes for the agent, for example offline media after open.</param>
public sealed record HostOutcome(ApiError? Error, string? Path, IReadOnlyList<string> Warnings)
{
    /// <summary>True on success.</summary>
    public bool Succeeded => Error is null;

    /// <summary>A success.</summary>
    /// <param name="path">The project path, or null.</param>
    /// <param name="warnings">Warnings, or null.</param>
    /// <returns>The outcome.</returns>
    public static HostOutcome Ok(string? path, IReadOnlyList<string>? warnings = null) => new(null, path, warnings ?? []);

    /// <summary>A failure.</summary>
    /// <param name="error">The error.</param>
    /// <returns>The outcome.</returns>
    public static HostOutcome Fail(ApiError error) => new(error, null, []);
}
