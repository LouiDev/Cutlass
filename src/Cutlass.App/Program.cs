using Avalonia;

namespace Cutlass.App;

/// <summary>The process entry point.</summary>
internal static class Program
{
    /// <summary>Starts the Avalonia desktop application.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The exit code (a smoke run returns 1 on failure).</returns>
    [STAThread]
    public static int Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Configures Avalonia. Also used by the visual designer.</summary>
    /// <returns>The configured builder.</returns>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
