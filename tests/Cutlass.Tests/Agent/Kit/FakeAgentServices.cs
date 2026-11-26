using Cutlass.Agent.Hosting;
using Cutlass.Compiler.Capabilities;
using Cutlass.Core.Editing;
using Cutlass.Media.Export;
using Cutlass.Media.Platform;
using Cutlass.Media.Preview;
using Cutlass.Media.Preview.Audio;
using Cutlass.Media.Projects;
using Cutlass.Media.Services;
using Cutlass.Media.Settings;
using Cutlass.Tests.TestSupport;

namespace Cutlass.Tests.Agent.Kit;

/// <summary>Services without ffmpeg: media is never ready.</summary>
internal sealed class FakeAgentServices : IAgentServices
{
    public FakeAgentServices(string root)
    {
        Paths = new AppPaths(
            Path.Combine(root, "settings", "settings.json"),
            Path.Combine(root, "data"),
            Path.Combine(root, "cache", "v1"),
            OperatingSystem.IsWindows());
    }

    public AppPaths Paths { get; }

    public AppLog? Log => null;

    public IIdGenerator Ids { get; } = new SequentialIdGenerator(100);

    public ProjectStore Projects { get; } = new();

    public AppSettings Settings { get; set; } = new();

    public bool IsMediaReady => false;

    public string? NotReadyReason => "ffmpeg is not available in unit tests.";

    public string? FfmpegVersion => null;

    public FfmpegCapabilities? Capabilities => null;

    public MediaCatalog? Catalog => null;

    public WaveformService? Waveforms => null;

    public ExportService? Export => null;

    public StillRenderer? Stills => null;

    public RangeAudioReader? AudioReader => null;
}
