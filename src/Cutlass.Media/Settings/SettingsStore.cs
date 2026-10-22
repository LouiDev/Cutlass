using System.Text.Json;

namespace Cutlass.Media.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON at <see cref="Platform.AppPaths.SettingsFile"/>. Saves are atomic:
/// write a unique temp file (<c>&lt;path&gt;.&lt;pid&gt;.&lt;8 hex&gt;.tmp</c>, as <see cref="Projects.ProjectStore"/> does, so two
/// instances saving at once never share one), flush, then rename over the old file.
/// </summary>
public sealed class SettingsStore
{
    /// <summary>Creates a store.</summary>
    /// <param name="settingsFile">The full path of <c>settings.json</c>.</param>
    public SettingsStore(string settingsFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFile);
        SettingsFile = Path.GetFullPath(settingsFile);
    }

    /// <summary>The settings file.</summary>
    public string SettingsFile { get; }

    /// <summary>
    /// Why the last <see cref="LoadAsync"/> fell back to the defaults although a file existed (corrupt JSON, moved
    /// aside to <c>settings.json.bad</c>, or unreadable), or null when it loaded normally. The app shows it as a toast.
    /// </summary>
    public string? LastLoadProblem { get; private set; }

    /// <summary>
    /// Loads the settings. A missing file gives the defaults; an unreadable or corrupt one is moved aside to
    /// <c>settings.json.bad</c> and the defaults are returned, so a broken file never blocks the app.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The settings.</returns>
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        LastLoadProblem = null;
        if (!File.Exists(SettingsFile))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = new FileStream(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var settings = await JsonSerializer.DeserializeAsync(stream, MediaJsonContext.Default.AppSettings, cancellationToken)
                .ConfigureAwait(false);
            return settings ?? new AppSettings();
        }
        catch (JsonException)
        {
            MoveAside();
            LastLoadProblem = "The settings file was damaged and has been reset to the defaults (the old file is settings.json.bad).";
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastLoadProblem = "The settings file could not be read, so the defaults are used: " + ex.Message;
            return new AppSettings();
        }
    }

    /// <summary>Saves the settings atomically.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        var temp = Projects.ProjectStore.TempPath(SettingsFile);
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, settings, MediaJsonContext.Default.AppSettings, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, SettingsFile, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Overwritten by the next save.
        }
    }

    private void MoveAside()
    {
        try
        {
            File.Move(SettingsFile, SettingsFile + ".bad", overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left in place; it is overwritten by the next save.
        }
    }
}
