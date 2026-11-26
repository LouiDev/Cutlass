namespace Cutlass.App.Services.Dialogs;

/// <summary>The answer to "Save changes?" before closing or replacing a dirty project.</summary>
public enum SaveChangesChoice
{
    /// <summary>Save, then continue.</summary>
    Save,

    /// <summary>Continue without saving.</summary>
    Discard,

    /// <summary>Stay with the current project.</summary>
    Cancel,
}
