using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Media.Audio;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Preview;

/// <summary>The services a <see cref="PlaybackSession"/> hands to each of its runs.</summary>
/// <param name="Runner">Starts ffmpeg.</param>
/// <param name="Toolset">The ffmpeg toolset.</param>
/// <param name="Capabilities">The capabilities.</param>
/// <param name="Work">The work directory.</param>
/// <param name="Catalog">The media catalog (unwrapped, for fingerprints).</param>
/// <param name="Fonts">Resolves title fonts (runs start one after another, so it is never used concurrently).</param>
/// <param name="Audio">The shared audio output.</param>
/// <param name="Cache">The frame cache shared with the frame server.</param>
internal sealed record PlaybackServices(
    ProcessRunner Runner,
    FfmpegToolset Toolset,
    FfmpegCapabilities Capabilities,
    WorkDirectory Work,
    IMediaCatalog Catalog,
    PreviewFontResolver Fonts,
    IAudioOutput Audio,
    FrameCache Cache);
