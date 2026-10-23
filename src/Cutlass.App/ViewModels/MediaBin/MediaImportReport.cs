using Cutlass.Core.Session;

namespace Cutlass.App.ViewModels.MediaBin;

/// <summary>
/// The options and results of an agent's import through the media bin (AGENT_INTEGRATION 2.2: the host imports the
/// way the bin does, with placeholders, posters and the media jobs). The bin commits each file as
/// <see cref="Writer"/> after <see cref="MayCommit"/> said yes, and reports instead of showing toasts. UI thread only.
/// </summary>
public sealed class MediaImportReport
{
    /// <summary>The writer of each commit (an agent's import runs as <see cref="SessionWriter.Agent"/>).</summary>
    public SessionWriter Writer { get; init; } = SessionWriter.Agent;

    /// <summary>Checked right before each commit (the agent still holds its lock), or null to always commit.</summary>
    public Func<bool>? MayCommit { get; init; }

    /// <summary>The files that could not be imported, with the reason.</summary>
    public List<(string Path, string Message)> Failed { get; } = [];

    /// <summary>Warnings such as a missing frame index.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>The requested files the project already held (not imported again).</summary>
    public List<string> AlreadyPresent { get; } = [];

    /// <summary>True when <see cref="MayCommit"/> said no and the import stopped.</summary>
    public bool Stopped { get; set; }
}
