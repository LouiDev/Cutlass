using Cutlass.Agent.Lock;

namespace Cutlass.Agent.Hosting;

/// <summary>How <see cref="HeadlessHost"/> starts (the <c>--project</c>, <c>--create</c>, <c>--allow-read</c> and <c>--allow-write</c> options).</summary>
public sealed record HeadlessHostOptions
{
    /// <summary>The project to load, or null for a new untitled project.</summary>
    public string? ProjectPath { get; init; }

    /// <summary>Create the project file when it is missing.</summary>
    public bool Create { get; init; }

    /// <summary>Extra read folders (<c>--allow-read</c>).</summary>
    public IReadOnlyList<string> ReadFolders { get; init; } = [];

    /// <summary>Extra write folders (<c>--allow-write</c>).</summary>
    public IReadOnlyList<string> WriteFolders { get; init; } = [];

    /// <summary>The lock timing, or null for the defaults.</summary>
    public LeaseOptions? Lease { get; init; }

    /// <summary>The clock, or null for the system clock.</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>True to tick the lock every second (false in tests that tick by hand).</summary>
    public bool StartLockDriver { get; init; } = true;
}
