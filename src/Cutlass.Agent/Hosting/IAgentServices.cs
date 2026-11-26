using Cutlass.Compiler.Capabilities;
using Cutlass.Core.Editing;
using Cutlass.Media.Export;
using Cutlass.Media.Platform;
using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;

namespace Cutlass.Agent.Hosting;

/// <summary>
/// The thread-safe media services the tools use (AGENT_INTEGRATION 2.2). The App adapts <c>AppServices</c>, the
/// headless host <see cref="HeadlessEngine"/>. The media members are null while ffmpeg is not ready.
/// </summary>
public interface IAgentServices
{
    /// <summary>The platform paths.</summary>
    AppPaths Paths { get; }

    /// <summary>The app log (<c>&lt;data&gt;/logs</c>), or null when the host keeps none.</summary>
    AppLog? Log { get; }

    /// <summary>Creates model ids.</summary>
    IIdGenerator Ids { get; }

    /// <summary>Loads and saves projects.</summary>
    ProjectStore Projects { get; }

    /// <summary>The current settings.</summary>
    AppSettings Settings { get; }

    /// <summary>True when ffmpeg is ready and the media services exist.</summary>
    bool IsMediaReady { get; }

    /// <summary>Why media is not ready (for <c>unavailable</c> errors), or null when ready.</summary>
    string? NotReadyReason { get; }

    /// <summary>The ffmpeg version text, or null.</summary>
    string? FfmpegVersion { get; }

    /// <summary>What the ffmpeg build can do, or null.</summary>
    FfmpegCapabilities? Capabilities { get; }

    /// <summary>The media the compiler renders from, or null.</summary>
    MediaCatalog? Catalog { get; }

    /// <summary>Waveform peaks, or null.</summary>
    WaveformService? Waveforms { get; }

    /// <summary>The export service, or null.</summary>
    ExportService? Export { get; }

    /// <summary>Renders timeline frames for agents, or null.</summary>
    StillRenderer? Stills { get; }

    /// <summary>Reads mixed timeline audio, or null.</summary>
    RangeAudioReader? AudioReader { get; }
}
