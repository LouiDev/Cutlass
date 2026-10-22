namespace Cutlass.Compiler.Export;

/// <summary>The quality choice of the export dialog (DESIGN 9). Each preset maps it to its own encoder value.</summary>
public enum ExportQuality
{
    /// <summary>Small and fast.</summary>
    Draft = 0,

    /// <summary>The everyday choice.</summary>
    Good = 1,

    /// <summary>High quality (the default).</summary>
    High = 2,

    /// <summary>Near transparent, large files.</summary>
    Best = 3,
}
