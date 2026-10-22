namespace Cutlass.IntegrationTests.Examples;

/// <summary>
/// Finds the example scripts of <c>build/examples</c> (AGENT_INTEGRATION 9.5) and the interpreters that run them:
/// Windows PowerShell or <c>pwsh</c>, and Python. A missing interpreter skips the tests that need it.
/// </summary>
internal static class ExampleScripts
{
    private static readonly Lazy<string?> PowerShellLazy = new(FindPowerShell);
    private static readonly Lazy<string?> PythonLazy =
        new(() => FindOnPath(OperatingSystem.IsWindows() ? ["python.exe", "python3.exe"] : ["python3", "python"]));

    /// <summary>The PowerShell executable, or null.</summary>
    public static string? PowerShell => PowerShellLazy.Value;

    /// <summary>The Python executable, or null.</summary>
    public static string? Python => PythonLazy.Value;

    /// <summary>The root of the repository the tests were built from (the folder holding <c>Cutlass.slnx</c>).</summary>
    public static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cutlass.slnx")))
            {
                dir = dir.Parent;
            }

            return dir?.FullName ?? throw new InvalidOperationException("Cutlass.slnx was not found above the test output.");
        }
    }

    /// <summary>The <c>build/examples</c> folder.</summary>
    public static string Directory => Path.Combine(RepositoryRoot, "build", "examples");

    /// <summary>The full path of an example script.</summary>
    /// <param name="name">The file name.</param>
    /// <returns>The path.</returns>
    public static string PathOf(string name) => Path.Combine(Directory, name);

    private static string? FindPowerShell()
    {
        // Windows PowerShell 5.1 first: the example promises to run there, and every Windows machine has it.
        if (OperatingSystem.IsWindows())
        {
            var builtIn = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(builtIn))
            {
                return builtIn;
            }
        }

        return FindOnPath(OperatingSystem.IsWindows() ? ["pwsh.exe"] : ["pwsh"]);
    }

    private static string? FindOnPath(string[] names)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
            {
                var candidate = Path.Combine(dir.Trim('"'), name);

                // The Windows Store alias of python.exe is a stub that opens the Store; skip it.
                if (File.Exists(candidate) && !candidate.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
