using Cutlass.Media.Settings;

namespace Cutlass.App.Services;

/// <summary>
/// What the Settings dialog (DESIGN 10) needs from the app: the current settings, the work that must run off the UI
/// thread (ffmpeg validation, cache measuring and clearing, the OpenAL device list) and the atomic save that applies
/// the result live. <c>AppServices</c> implements it; tests use a fake.
/// </summary>
public interface ISettingsHost
{
    /// <summary>The settings in effect.</summary>
    AppSettings Settings { get; }

    /// <summary>The cache folder (read-only in the dialog).</summary>
    string CacheFolder { get; }

    /// <summary>True when the media services already run on a working ffmpeg, so a new location applies on the next start.</summary>
    bool IsFfmpegReady { get; }

    /// <summary>The ffmpeg in use for the About page, for example "N-126308 (libavfilter 12.3), C:/ffmpeg/bin/ffmpeg.exe".</summary>
    string FfmpegSummary { get; }

    /// <summary>Checks an ffmpeg location off the UI thread: the path rules, <c>-version</c> and the capability probe.</summary>
    /// <param name="path">The ffmpeg executable or its folder, or null or empty to search like startup does (PATH and the well-known folders).</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>The result.</returns>
    Task<FfmpegCheck> CheckFfmpegAsync(string? path, CancellationToken cancellationToken);

    /// <summary>Measures the cache off the UI thread.</summary>
    /// <param name="cancellationToken">Cancels the measure.</param>
    /// <returns>The bytes in use.</returns>
    Task<long> MeasureCacheAsync(CancellationToken cancellationToken);

    /// <summary>Deletes every cache entry the open project does not use, off the UI thread ("Clear cache").</summary>
    /// <param name="cancellationToken">Cancels before the deletion starts.</param>
    /// <returns>The bytes in use afterwards.</returns>
    Task<long> ClearCacheAsync(CancellationToken cancellationToken);

    /// <summary>Lists the OpenAL output devices off the UI thread (TECH_BRIEF 5.6).</summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The device names, empty when OpenAL is unavailable.</returns>
    Task<IReadOnlyList<string>> ListAudioDevicesAsync(CancellationToken cancellationToken);

    /// <summary>Saves the settings atomically and applies them live (cache limit, proxy policy, then <c>SettingsChanged</c>).</summary>
    /// <param name="settings">The new settings.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>A task that completes when the file is written and the change applied.</returns>
    Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken);
}
