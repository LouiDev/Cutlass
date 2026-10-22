using Cutlass.Media.Settings;

namespace Cutlass.App.Services;

/// <summary>The settings before and after the Settings dialog applied them (<c>AppServices.SettingsChanged</c>).</summary>
public sealed class SettingsChangedEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="oldSettings">The settings before.</param>
    /// <param name="newSettings">The settings now in effect (already saved).</param>
    public SettingsChangedEventArgs(AppSettings oldSettings, AppSettings newSettings)
    {
        ArgumentNullException.ThrowIfNull(oldSettings);
        ArgumentNullException.ThrowIfNull(newSettings);
        OldSettings = oldSettings;
        NewSettings = newSettings;
    }

    /// <summary>The settings before the change.</summary>
    public AppSettings OldSettings { get; }

    /// <summary>The settings now in effect.</summary>
    public AppSettings NewSettings { get; }
}
