using System.Globalization;
using Cutlass.App.Services;
using Cutlass.Compiler.Capabilities;
using Cutlass.Media.Audio;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Settings;

namespace Cutlass.App;

/// <summary>
/// The Settings dialog's side of the composition root (DESIGN 10): <see cref="ISettingsHost"/> and the
/// <see cref="SettingsChanged"/> event other parts (autosave, the main window) follow to apply changes live.
/// </summary>
public sealed partial class AppServices : ISettingsHost
{
    /// <summary>
    /// Raised after <see cref="ApplySettingsAsync"/> saved new settings and applied the cache limit and proxy policy, on
    /// the caller's thread (the UI thread for the Settings dialog). Subscribers apply what they own: the autosave
    /// interval, the viewer defaults, default durations and so on.
    /// </summary>
    public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

    /// <inheritdoc />
    public string CacheFolder => Paths.CacheRoot;

    /// <summary>The folder for log files (Help, "Open log folder"): <c>&lt;data&gt;/logs</c>, where crash logs go.</summary>
    public string LogFolder => Paths.LogsDirectory;

    /// <inheritdoc />
    public bool IsFfmpegReady => FfmpegState == FfmpegState.Ready;

    /// <inheritdoc />
    public string FfmpegSummary => Toolset is { } toolset
        ? toolset.Version.BuildId + " (libavfilter " +
          toolset.Version.AvfilterMajor.ToString(CultureInfo.InvariantCulture) + "." +
          toolset.Version.AvfilterMinor.ToString(CultureInfo.InvariantCulture) + "), " + toolset.FfmpegPath
        : "not found";

    /// <inheritdoc />
    public Task<FfmpegCheck> CheckFfmpegAsync(string? path, CancellationToken cancellationToken) =>
        Task.Run(() => CheckFfmpegCoreAsync(path, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<long> MeasureCacheAsync(CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                if (MediaCache is { } cache)
                {
                    cache.Measure();
                    return cache.UsedBytes;
                }

                return DirectorySize(Paths.CacheRoot);
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task<long> ClearCacheAsync(CancellationToken cancellationToken)
    {
        // TECH_BRIEF 6.1: the pipeline keeps the media a running job works on and detaches cleared proxies.
        if (Pipeline is { } pipeline)
        {
            await pipeline.ClearCacheAsync(cancellationToken);
        }

        return await MeasureCacheAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListAudioDevicesAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<string>>(
            () =>
            {
                try
                {
                    return OpenAlAudioOutput.ListDevices();
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or BadImageFormatException)
                {
                    // No OpenAL: only "System default" is offered.
                    return [];
                }
            },
            cancellationToken);

    /// <inheritdoc />
    public async Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await SettingsStore.SaveAsync(settings, cancellationToken);
        var old = Settings;
        Settings = settings;

        // Live: the cache limit (an LRU pass when it shrank) and the proxy policy of new media jobs.
        if (MediaCache is { } cache)
        {
            var limit = Math.Max(1, settings.CacheLimitGb) * MediaCache.BytesPerGb;
            var shrank = limit < cache.LimitBytes;
            cache.LimitBytes = limit;
            if (shrank)
            {
                var keep = Pipeline?.CurrentProtected() ?? [];
                _ = Task.Run(() =>
                {
                    try
                    {
                        cache.Evict(keep);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // The LRU runs again after the next job.
                    }
                });
            }
        }

        if (Pipeline is { } pipeline)
        {
            pipeline.ProxyPolicy = settings.ProxyPolicy;
        }

        SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(old, settings));
    }

    private async Task<FfmpegCheck> CheckFfmpegCoreAsync(string? path, CancellationToken cancellationToken)
    {
        FfmpegToolset? toolset;
        Work.EnsureCreated();
        if (string.IsNullOrWhiteSpace(path))
        {
            toolset = await FfmpegLocator.ForCurrentPlatform().FindAsync(null, Runner, Work.Path, cancellationToken).ConfigureAwait(false);
            if (toolset is null)
            {
                return new FfmpegCheck(false, "not found on PATH or in the usual folders", null);
            }
        }
        else
        {
            if (FfmpegPathCheck.ForCurrentPlatform().Check(path, out var exe) is { } problem)
            {
                return new FfmpegCheck(false, problem, null);
            }

            try
            {
                toolset = await FfmpegToolset.ValidateAsync(exe!, Runner, Work.Path, cancellationToken).ConfigureAwait(false);
            }
            catch (FfmpegException ex)
            {
                return new FfmpegCheck(false, ex.Message, exe);
            }
        }

        FfmpegCapabilities caps;
        try
        {
            // Re-detect: the probe runs again even when capabilities.json has this executable.
            caps = await new CapabilityProbe(Runner, Work, Paths.CapabilitiesFile)
                .ProbeAsync(toolset, cancellationToken, forceRefresh: true)
                .ConfigureAwait(false);
        }
        catch (FfmpegException ex)
        {
            return new FfmpegCheck(false, "unusable: " + ex.Message, toolset.FfmpegPath);
        }

        var found = FfmpegCheckText(toolset.Version.BuildId, toolset.Version.AvfilterMajor);
        if (!caps.CanRender)
        {
            return new FfmpegCheck(false, found + ", but it lacks required filters: " + string.Join(", ", caps.MissingRequiredFilters), toolset.FfmpegPath);
        }

        return new FfmpegCheck(true, found + (caps.HasDrawtext ? string.Empty : " (no drawtext: titles are disabled)"), toolset.FfmpegPath);
    }

    /// <summary>The DESIGN 10 status line of a found ffmpeg, for example "found: N-126308, libavfilter 12".</summary>
    /// <param name="buildId">The build id.</param>
    /// <param name="avfilterMajor">The libavfilter major version.</param>
    /// <returns>The text.</returns>
    public static string FfmpegCheckText(string buildId, int avfilterMajor) =>
        "found: " + buildId + ", libavfilter " + avfilterMajor.ToString(CultureInfo.InvariantCulture);
}
