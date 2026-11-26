namespace Cutlass.App.Services.Dialogs;

/// <summary>The answer to the recovery question (DESIGN 11).</summary>
public enum RecoveryChoice
{
    /// <summary>Decide later: open the project as saved and keep the autosave (the welcome list still offers it).</summary>
    Ignore,

    /// <summary>Open the autosaved state (unsaved, so the project shows as dirty).</summary>
    Recover,

    /// <summary>Delete the autosave.</summary>
    Discard,
}
