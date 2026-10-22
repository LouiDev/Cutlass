using Cutlass.Agent.Hosting;
using Cutlass.Agent.Http;
using Cutlass.Agent.Lock;
using Cutlass.Agent.Tools;
using Cutlass.IntegrationTests.Infrastructure;
using Cutlass.Media.Platform;

namespace Cutlass.IntegrationTests.Agent;

/// <summary>
/// A headless engine and host with the HTTP API on a free loopback port, in a temp folder (AGENT_INTEGRATION 7.2).
/// The read folders are the fixture folder and the kit's media folder; the write folder is the kit's output folder.
/// Dispose checks that no ffmpeg process of the engine is left.
/// </summary>
internal sealed class AgentKit : IAsyncDisposable
{
    public const string Token = "integration-token-0123456789abcdefghijklmnop";

    private AgentKit(TempDirectory temp, HeadlessEngine engine, HeadlessHost host, ToolDispatcher dispatcher, AgentHttpServer server)
    {
        Temp = temp;
        Engine = engine;
        Host = host;
        Dispatcher = dispatcher;
        Server = server;
        Client = new AgentClient(server.BaseUrl!, Token) { Timeout = TimeSpan.FromMinutes(3) };
    }

    public TempDirectory Temp { get; }

    public HeadlessEngine Engine { get; }

    public HeadlessHost Host { get; }

    public ToolDispatcher Dispatcher { get; }

    public AgentHttpServer Server { get; }

    public AgentClient Client { get; }

    public string MediaDirectory => Temp.Combine("media");

    public string OutputDirectory => Temp.Combine("out");

    public string ProjectPath => Temp.Combine("projects", "edit.cutlass");

    public static async Task<AgentKit> CreateAsync(TestMediaFixture media, LeaseOptions? lease = null)
    {
        var temp = new TempDirectory("cutlass-agent-it");
        Directory.CreateDirectory(temp.Combine("media"));
        Directory.CreateDirectory(temp.Combine("out"));
        var paths = new AppPaths(temp.Combine("settings", "settings.json"), temp.Combine("data"), temp.Combine("cache", "v1"), OperatingSystem.IsWindows());
        var engine = await HeadlessEngine.StartAsync(paths, TestFfmpeg.FfmpegPath, AppContext.BaseDirectory, installShutdownHooks: false);
        Assert.True(engine.IsMediaReady, engine.NotReadyReason);
        var host = await HeadlessHost.CreateAsync(
            engine,
            new HeadlessHostOptions
            {
                ProjectPath = temp.Combine("projects", "edit.cutlass"),
                Create = true,
                ReadFolders = [media.Directory, temp.Combine("media")],
                WriteFolders = [temp.Combine("out")],
                Lease = lease,
            });
        var dispatcher = new ToolDispatcher(host, new AuditLog(engine.Log), null, "0.0.0-it");
        var server = new AgentHttpServer(dispatcher, Token);
        await server.StartAsync(Random.Shared.Next(49_200, 64_000));
        return new AgentKit(temp, engine, host, dispatcher, server);
    }

    /// <summary>Generates a WAV in the media folder with an aevalsrc expression.</summary>
    public async Task<string> AudioAsync(string name, string expression, double seconds)
    {
        var path = Path.Combine(MediaDirectory, name);
        var result = await TestProcess.RunAsync(
            TestFfmpeg.FfmpegPath,
            ["-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i",
                $"aevalsrc='{expression}':s=48000:d={seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}", "-c:a", "pcm_s16le", path],
            MediaDirectory,
            TimeSpan.FromMinutes(1));
        Assert.True(result.ExitCode == 0, result.Stderr);
        return path;
    }

    /// <summary>Decodes a PNG to BGRA with ffmpeg.</summary>
    public static async Task<(int Width, int Height, byte[] Bgra)> DecodePngAsync(byte[] png, string folder, int width, int height)
    {
        var file = Path.Combine(folder, "decode-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        await File.WriteAllBytesAsync(file, png);
        var result = await TestProcess.RunAsync(
            TestFfmpeg.FfmpegPath,
            ["-hide_banner", "-v", "error", "-i", file, "-f", "rawvideo", "-pix_fmt", "bgra", "pipe:1"],
            folder,
            TimeSpan.FromMinutes(1));
        Assert.True(result.ExitCode == 0, result.Stderr);
        Assert.Equal(width * height * 4, result.StdoutBytes.Length);
        return (width, height, result.StdoutBytes);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Server.DisposeAsync();
        Dispatcher.Dispose();
        await Host.DisposeAsync();

        // No ffmpeg of this engine may outlive the work (AGENT_INTEGRATION 7.2).
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (Engine.Registry.Count > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        var left = Engine.Registry.Count;
        await Engine.DisposeAsync();
        Temp.Dispose();
        Assert.Equal(0, left);
    }
}
