using System.Security.Cryptography;
using System.Text;

namespace Cutlass.Agent.Http;

/// <summary>
/// The bearer token of the agent API (AGENT_INTEGRATION 4.1): 32 random bytes in base64url, kept in
/// <c>&lt;data&gt;/agent/token</c>. On Linux the folder is created 0700 and the file 0600 through
/// <see cref="UnixFileMode"/> at creation (never chmod afterwards); a token readable by others is regenerated. On
/// Windows the per-user local app data ACL protects it.
/// </summary>
public sealed class TokenStore
{
    /// <summary>The token file name.</summary>
    public const string FileName = "token";

    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Creates the store.</summary>
    /// <param name="agentDirectory">The agent folder, <c>AppPaths.AgentDirectory</c>.</param>
    public TokenStore(string agentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentDirectory);
        Directory = Path.GetFullPath(agentDirectory);
        TokenFile = Path.Combine(Directory, FileName);
    }

    /// <summary>The agent folder.</summary>
    public string Directory { get; }

    /// <summary>The token file.</summary>
    public string TokenFile { get; }

    /// <summary>Creates a new random token: 32 bytes in base64url.</summary>
    /// <returns>The token.</returns>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Compares a presented token with the expected one in constant time.</summary>
    /// <param name="expected">The real token.</param>
    /// <param name="presented">The token from the request, or null.</param>
    /// <returns>True when equal.</returns>
    public static bool Matches(string expected, string? presented)
    {
        if (presented is null)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(presented));
    }

    /// <summary>
    /// Reads the token, creating it when missing or unreadable. On Linux a file readable by group or others is
    /// replaced by a new token.
    /// </summary>
    /// <returns>The token.</returns>
    public string LoadOrCreate()
    {
        EnsureDirectory();
        if (File.Exists(TokenFile))
        {
            var insecure = !OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(TokenFile) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite)) != 0;
            if (!insecure)
            {
                var text = File.ReadAllText(TokenFile).Trim();
                if (text.Length >= 32)
                {
                    return text;
                }
            }
        }

        return Regenerate();
    }

    /// <summary>Replaces the token with a new one ("Regenerate token").</summary>
    /// <returns>The new token.</returns>
    public string Regenerate()
    {
        EnsureDirectory();
        var token = NewToken();
        var temp = Path.Combine(Directory, FileName + "." + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".tmp");
        var options = new FileStreamOptions { Mode = System.IO.FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = FileMode;
        }

        using (var stream = new FileStream(temp, options))
        {
            var bytes = Encoding.UTF8.GetBytes(token);
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temp, TokenFile, overwrite: true);
        return token;
    }

    private void EnsureDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        else
        {
            System.IO.Directory.CreateDirectory(Directory, DirectoryMode);
        }
    }
}
