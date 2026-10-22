namespace Cutlass.Compiler;

/// <summary>
/// Why a timeline range is compiled (TECH_BRIEF 7.1). Every purpose goes through the same compiler path; the purpose
/// only picks the scale quality (bicubic for export, bilinear for previews).
/// </summary>
public enum RenderPurpose
{
    /// <summary>A file export.</summary>
    Export = 0,

    /// <summary>One scrub or step frame, piped as rawvideo.</summary>
    PreviewFrame = 1,

    /// <summary>A playback or prefetch video window, piped as rawvideo.</summary>
    PreviewVideo = 2,

    /// <summary>A playback audio window, piped as PCM.</summary>
    PreviewAudio = 3,
}
