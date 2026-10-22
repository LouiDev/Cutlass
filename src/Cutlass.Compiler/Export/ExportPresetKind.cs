namespace Cutlass.Compiler.Export;

/// <summary>What an export preset produces.</summary>
public enum ExportPresetKind
{
    /// <summary>A video file with audio.</summary>
    Video = 0,

    /// <summary>An animated GIF: video only, palette tail, chosen width.</summary>
    Gif = 1,

    /// <summary>An audio-only file, compiled with <c>IncludeVideo=false</c>.</summary>
    Audio = 2,
}
