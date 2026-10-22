using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Cutlass.Agent.Api;
using Cutlass.Media.Projects;

namespace Cutlass.Agent.Http;

/// <summary>
/// <c>&lt;data&gt;/agent/endpoint.json</c> (AGENT_INTEGRATION 4.1): where the serving process listens. Written
/// atomically (temp file and rename, 0600 on Linux) and deleted on clean shutdown. A file whose pid and start time
/// name a live process means another Cutlass serves agents, and a second server does not start.
/// </summary>
public sealed class EndpointFile
{
    /// <summary>The file name.</summary>
    public const string FileName = "endpoint.json";

    /// <summary>Creates the accessor.</summary>
    /// <param name="agentDirectory">The agent folder, <c>AppPaths.AgentDirectory</c>.</param>
    public EndpointFile(string agentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentDirectory);
        FilePath = Path.Combine(Path.GetFullPath(agentDirectory), FileName);
    }

    /// <summary>The file path.</summary>
    public string FilePath { get; }

    /// <summary>The endpoint of this process.</summary>
    /// <param name="baseUrl">The base URL.</param>
    /// <param name="hostKind"><c>app</c> or <c>headless</c>.</param>
    /// <param name="tokenFile">The token file.</param>
    /// <param name="projectPath">The open project, or null.</param>
    /// <returns>The info.</returns>
    public static EndpointInfo ForThisProcess(string baseUrl, string hostKind, string tokenFile, string? projectPath)
    {
        using var self = Process.GetCurrentProcess();
        return new EndpointInfo
        {
            BaseUrl = baseUrl,
            Pid = Environment.ProcessId,
            ProcessStartUtc = DateTime.SpecifyKind(new DateTime(self.StartTime.ToUniversalTime().Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond), DateTimeKind.Utc),
            HostKind = hostKind,
            TokenFile = tokenFile.Replace('\\', '/'),
            ProjectPath = projectPath?.Replace('\\', '/'),
        };
    }

    /// <summary>True when the info names a live process other than this one.</summary>
    /// <param name="info">The info.</param>
    /// <returns>True when another live process serves.</returns>
    public static bool IsLiveOther(EndpointInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return info.Pid != Environment.ProcessId && AutosaveRecovery.IsProcessAlive(info.Pid, info.ProcessStartUtc.ToUniversalTime().Ticks);
    }

    /// <summary>Reads the file.</summary>
    /// <returns>The info, or null when missing or unreadable.</returns>
    public EndpointInfo? Read()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(FilePath), AgentJsonContext.Relaxed.EndpointInfo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Reads the file and returns it when a live process other than this one owns it.</summary>
    /// <returns>The live owner's info, or null.</returns>
    public EndpointInfo? ReadLiveOther() => Read() is { } info && IsLiveOther(info) ? info : null;

    /// <summary>Writes the file atomically.</summary>
    /// <param name="info">The info.</param>
    public void Write(EndpointInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var directory = Path.GetDirectoryName(FilePath)!;
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var temp = FilePath + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temp, options))
        {
            JsonSerializer.Serialize(stream, info, AgentJsonContext.Indented.EndpointInfo);
            stream.Flush(true);
        }

        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Deletes the file when this process owns it (clean shutdown).</summary>
    public void DeleteIfOwned()
    {
        try
        {
            if (Read() is { } info && info.Pid != Environment.ProcessId)
            {
                return;
            }

            File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover file names a dead process, which readers ignore.
        }
    }
}
