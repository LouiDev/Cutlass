namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>A fresh folder under the system temp directory, deleted (best effort) on dispose.</summary>
public sealed class TempDirectory : IDisposable
{
    /// <summary>Creates the folder.</summary>
    /// <param name="prefix">A readable prefix for the folder name.</param>
    public TempDirectory(string prefix = "cutlass-it")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Path);
    }

    /// <summary>The full path of the folder.</summary>
    public string Path { get; }

    /// <summary>Combines a relative path with the folder.</summary>
    /// <param name="parts">The relative parts.</param>
    /// <returns>The full path.</returns>
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A process may still hold a handle for a moment; the temp folder is cleaned by the OS eventually.
        }
    }
}
