using Cutlass.Media.Platform;

namespace Cutlass.Media.Projects;

/// <summary>What the file system says about a media file, for the link check (DESIGN 11).</summary>
/// <param name="SizeBytes">The file size.</param>
/// <param name="MtimeUtc">The last write time (UTC).</param>
public sealed record MediaFileFacts(long SizeBytes, DateTime MtimeUtc)
{
    /// <summary>
    /// Reads a file's facts, or null when it does not exist, cannot be read or is not a regular file: a project can
    /// name a device or a FIFO, which must never be fingerprinted or probed.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The facts, or null.</returns>
    public static MediaFileFacts? Of(string path)
    {
        try
        {
            if (FileIdentity.KindOf(path) != FileKind.Regular)
            {
                return null;
            }

            var info = new FileInfo(path);
            return info.Exists ? new MediaFileFacts(info.Length, info.LastWriteTimeUtc) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
