using System.Runtime.InteropServices;
using Cutlass.App.ViewModels;

namespace Cutlass.App;

/// <summary>
/// The crash hooks of the application (DESIGN 11): log the failure and write one last autosave, also when Linux ends
/// the process with SIGTERM, SIGHUP or SIGINT (logout, a closed terminal, <c>kill</c>).
/// </summary>
public sealed partial class App
{
    /// <summary>How long the last autosave of a crashing process may take.</summary>
    private static readonly TimeSpan CrashAutosaveTimeout = TimeSpan.FromSeconds(5);

    // Kept alive for the lifetime of the process: a collected registration unregisters its handler.
    private static readonly List<PosixSignalRegistration> s_signals = [];

    private static int s_crashHandled;

    // An unhandled exception ends the process: log it to <data>/logs and save the dirty project once, best effort.
    // An unobserved task exception does not end it (.NET 4.5+), but it is logged and autosaved (through the normal
    // autosave path) because it means some work silently failed.
    private static void InstallCrashHandlers(MainWindowViewModel vm)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (Interlocked.Exchange(ref s_crashHandled, 1) == 0)
            {
                vm.HandleCrash(
                    e.IsTerminating ? "Unhandled exception (terminating)" : "Unhandled exception",
                    e.ExceptionObject as Exception,
                    CrashAutosaveTimeout,
                    e.IsTerminating);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // The process keeps running: the autosave goes through the normal, tracked path (not the blocking one).
            vm.HandleCrash("Unobserved task exception", e.Exception, CrashAutosaveTimeout, terminating: false);
            e.SetObserved();
        };
        InstallSignalHandlers(vm);
    }

    // Linux (and other POSIX systems): SIGTERM, SIGHUP and SIGINT end the app without the window's close prompt. The
    // handler runs before the default action (for SIGTERM the runtime's ProcessExit, where the child registry kills
    // the ffmpeg children) and writes the bounded last-chance autosave. On Windows nothing is registered: a console
    // control event does not reach this GUI app, and a session end closes the window normally.
    private static void InstallSignalHandlers(MainWindowViewModel vm)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGINT })
        {
            try
            {
                s_signals.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    if (Interlocked.Exchange(ref s_crashHandled, 1) == 0)
                    {
                        vm.HandleCrash("Ended by " + context.Signal, null, CrashAutosaveTimeout);
                    }
                }));
            }
            catch (Exception ex) when (ex is PlatformNotSupportedException or IOException)
            {
                // Without the hook a signal ends the app as before, without the last autosave.
            }
        }
    }
}
