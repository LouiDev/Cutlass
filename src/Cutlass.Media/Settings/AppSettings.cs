namespace Cutlass.Media.Settings;

/// <summary>
/// The user settings stored in <c>settings.json</c> (DESIGN 10). Missing fields keep their defaults, so older files
/// load unchanged. Treat an instance as a value: change settings with <c>with</c> and save the copy.
/// </summary>
/// <remarks>
/// The properties are settable, not init-only, because the System.Text.Json source generator assigns every
/// init-only property in the object initializer and so overwrites the defaults of fields missing from the file.
/// </remarks>
public sealed partial record AppSettings
{
    /// <summary>The current settings file version.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The settings file version.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>The longest time between two autosaves of a dirty project, in minutes.</summary>
    public int AutosaveIntervalMinutes { get; set; } = 2;

    /// <summary>The default video and audio transition duration in seconds.</summary>
    public double DefaultTransitionSeconds { get; set; } = 1.0;

    /// <summary>
    /// The default video transition of Ctrl+D (DESIGN 4.2: double-click in the Effects tab sets it), as a
    /// <c>TransitionKind</c> name. A string, not the enum, so an unknown name falls back to Crossfade instead of
    /// making the whole file unreadable.
    /// </summary>
    public string DefaultTransition { get; set; } = "Crossfade";

    /// <summary>The default duration of stills in seconds.</summary>
    public double DefaultStillSeconds { get; set; } = 5.0;

    /// <summary>The default duration of titles in seconds.</summary>
    public double DefaultTitleSeconds { get; set; } = 5.0;

    /// <summary>Ask before deleting a track that holds clips.</summary>
    public bool ConfirmDeleteTracks { get; set; } = true;

    /// <summary>The ffmpeg executable or its folder chosen by the user, or null to search automatically.</summary>
    public string? FfmpegPath { get; set; }

    /// <summary>When proxies are built.</summary>
    public ProxyPolicy ProxyPolicy { get; set; } = ProxyPolicy.Auto;

    /// <summary>A custom cache folder, or null for the platform default.</summary>
    public string? CacheFolder { get; set; }

    /// <summary>The cache size limit in GB.</summary>
    public int CacheLimitGb { get; set; } = 20;

    /// <summary>The default viewer quality.</summary>
    public PreviewQuality DefaultPreviewQuality { get; set; } = PreviewQuality.Auto;

    /// <summary>The OpenAL output device name, or null for the system default.</summary>
    public string? AudioDevice { get; set; }

    /// <summary>Use proxies for preview when they exist.</summary>
    public bool UseProxies { get; set; } = true;

    /// <summary>The top row's share of the window height (timeline gets the rest).</summary>
    public double TopRowFraction { get; set; } = 0.58;

    /// <summary>The Media panel's share of the top row width.</summary>
    public double MediaColumnFraction { get; set; } = 0.22;

    /// <summary>The Inspector panel's share of the top row width.</summary>
    public double InspectorColumnFraction { get; set; } = 0.22;
}
