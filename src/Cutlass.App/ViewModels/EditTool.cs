namespace Cutlass.App.ViewModels;

/// <summary>The timeline tools (DESIGN 5.4), selected with V, B, N, Y and C.</summary>
public enum EditTool
{
    /// <summary>Select (V): move, trim one side.</summary>
    Select,

    /// <summary>Ripple (B): ripple trim.</summary>
    Ripple,

    /// <summary>Roll (N): move a shared cut.</summary>
    Roll,

    /// <summary>Slip (Y): slip source in and out.</summary>
    Slip,

    /// <summary>Razor (C): split at the clicked frame.</summary>
    Razor,
}
