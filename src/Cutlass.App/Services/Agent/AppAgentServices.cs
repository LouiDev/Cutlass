using Cutlass.Agent.Hosting;
using Cutlass.Compiler.Capabilities;
using Cutlass.Core.Editing;
using Cutlass.Media.Export;
using Cutlass.Media.Platform;
using Cutlass.Media.Preview;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;

namespace Cutlass.App.Services.Agent;

/// <summary>
/// Adapts <see cref="AppServices"/> to <see cref="IAgentServices"/> (AGENT_INTEGRATION 2.4). Every member reads the
/// composition root live, so services created later (once ffmpeg is found) show up without a new adapter.
/// </summary>
public sealed class AppAgentServices : IAgentServices
{
    private readonly AppServices _services;

    /// <summary>Creates the adapter.</summary>
    /// <param name="services">The composition root.</param>
    /// <param name="log">The app log, or null.</param>
    public AppAgentServices(AppServices services, AppLog? log)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        Log = log;
    }

    /// <inheritdoc />
    public AppPaths Paths => _services.Paths;

    /// <inheritdoc />
    public AppLog? Log { get; }

    /// <inheritdoc />
    public IIdGenerator Ids => _services.Ids;

    /// <inheritdoc />
    public ProjectStore Projects => _services.Projects;

    /// <inheritdoc />
    public AppSettings Settings => _services.Settings;

    /// <inheritdoc />
    public bool IsMediaReady => _services.FfmpegState == FfmpegState.Ready && _services.Catalog is not null;

    /// <inheritdoc />
    public string? NotReadyReason => IsMediaReady
        ? null
        : _services.FfmpegState switch
        {
            FfmpegState.Checking => "Cutlass is still looking for ffmpeg. Try again in a few seconds.",
            FfmpegState.Unusable => "The ffmpeg Cutlass found cannot render. Ask the user to install a full ffmpeg build.",
            _ => "ffmpeg was not found. Ask the user to install ffmpeg or set its location in Settings > Media.",
        };

    /// <inheritdoc />
    public string? FfmpegVersion => _services.Toolset?.Version.DisplayText;

    /// <inheritdoc />
    public FfmpegCapabilities? Capabilities => _services.Capabilities;

    /// <inheritdoc />
    public MediaCatalog? Catalog => _services.Catalog;

    /// <inheritdoc />
    public WaveformService? Waveforms => _services.Waveforms;

    /// <inheritdoc />
    public ExportService? Export => _services.Export;

    /// <inheritdoc />
    public StillRenderer? Stills => _services.Stills;

    /// <inheritdoc />
    public RangeAudioReader? AudioReader => _services.AudioReader;
}
