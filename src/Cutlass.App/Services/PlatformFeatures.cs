namespace Cutlass.App.Services;

/// <summary>
/// What the desktop platform offers the app, where it differs between Windows and Linux.
/// </summary>
public static class PlatformFeatures
{
    /// <summary>
    /// True when files dragged from the file manager can be dropped into the app. Avalonia 11.3's X11 backend (which
    /// Linux uses, also under XWayland) has no XDND support, so OS file drops never arrive on Linux; drags inside the
    /// app (media bin, Source monitor and Effects items onto the timeline) work everywhere. TECH_BRIEF 5.6.
    /// </summary>
    public static bool OsFileDrops { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
}
