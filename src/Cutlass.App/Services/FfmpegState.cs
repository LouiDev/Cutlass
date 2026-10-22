namespace Cutlass.App.Services;

/// <summary>The ffmpeg state the status bar and the welcome overlay show (DESIGN 4.5).</summary>
public enum FfmpegState
{
    /// <summary>Discovery and the capability probe are running.</summary>
    Checking,

    /// <summary>A usable ffmpeg was found: import, preview and export work.</summary>
    Ready,

    /// <summary>No ffmpeg was found: import and export are disabled, the install hint shows.</summary>
    NotFound,

    /// <summary>An ffmpeg was found but cannot render (too old or missing required filters).</summary>
    Unusable,
}
