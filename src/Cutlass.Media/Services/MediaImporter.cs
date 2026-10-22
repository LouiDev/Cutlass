using System.Buffers.Binary;
using System.Security.Cryptography;
using Cutlass.Compiler;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Services;

/// <summary>
/// Imports one media file: probes it (TECH_BRIEF 6.3), computes its content fingerprint (6.2), builds its frame
/// index (6.4) and poster (6.5) in parallel, creates the <see cref="MediaAsset"/> and registers it in the
/// <see cref="MediaCatalog"/>. The caller adds the asset to the project. Runs entirely off the UI thread.
/// </summary>
public sealed class MediaImporter
{
    /// <summary>The size of each fingerprint chunk: 1 MiB.</summary>
    public const int FingerprintChunk = 1024 * 1024;

    private readonly ProbeService _probe;
    private readonly PosterService _posters;
    private readonly FrameIndexService _frameIndex;
    private readonly MediaCatalog _catalog;
    private readonly IIdGenerator _ids;
    private readonly Fingerprinter? _fingerprints;

    /// <summary>Creates the importer.</summary>
    /// <param name="probe">Probes files.</param>
    /// <param name="posters">Renders posters.</param>
    /// <param name="frameIndex">Builds frame indexes.</param>
    /// <param name="catalog">Receives the imported assets.</param>
    /// <param name="ids">Creates media ids.</param>
    public MediaImporter(ProbeService probe, PosterService posters, FrameIndexService frameIndex, MediaCatalog catalog, IIdGenerator ids)
        : this(probe, posters, frameIndex, catalog, ids, null)
    {
    }

    /// <summary>Creates the importer with a fingerprint memo (TECH_BRIEF 6.2).</summary>
    /// <param name="probe">Probes files.</param>
    /// <param name="posters">Renders posters.</param>
    /// <param name="frameIndex">Builds frame indexes.</param>
    /// <param name="catalog">Receives the imported assets.</param>
    /// <param name="ids">Creates media ids.</param>
    /// <param name="fingerprints">The fingerprint memo, or null to hash every import.</param>
    public MediaImporter(ProbeService probe, PosterService posters, FrameIndexService frameIndex, MediaCatalog catalog, IIdGenerator ids, Fingerprinter? fingerprints)
    {
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(posters);
        ArgumentNullException.ThrowIfNull(frameIndex);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(ids);
        _probe = probe;
        _posters = posters;
        _frameIndex = frameIndex;
        _catalog = catalog;
        _ids = ids;
        _fingerprints = fingerprints;
    }

    /// <summary>
    /// The content fingerprint (TECH_BRIEF 6.2): <c>sha256:</c> plus the hex SHA-256 over the file size as 8 bytes
    /// little-endian, then the first, middle and last 1 MiB of the file, or the whole file when it is under 3 MiB.
    /// The middle chunk starts at <c>(size - 1 MiB) / 2</c>. Path and mtime are not part of it.
    /// </summary>
    /// <remarks>Without memo; <see cref="Fingerprinter"/> adds the <c>fingerprints.json</c> memo around it.</remarks>
    /// <param name="path">The file.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The fingerprint.</returns>
    public static async Task<string> ComputeFingerprintAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        var size = stream.Length;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sizeBytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(sizeBytes, size);
        hash.AppendData(sizeBytes);

        var buffer = new byte[FingerprintChunk];
        if (size < 3L * FingerprintChunk)
        {
            await AppendRangeAsync(stream, hash, buffer, 0, size, ct).ConfigureAwait(false);
        }
        else
        {
            await AppendRangeAsync(stream, hash, buffer, 0, FingerprintChunk, ct).ConfigureAwait(false);
            await AppendRangeAsync(stream, hash, buffer, (size - FingerprintChunk) / 2, FingerprintChunk, ct).ConfigureAwait(false);
            await AppendRangeAsync(stream, hash, buffer, size - FingerprintChunk, FingerprintChunk, ct).ConfigureAwait(false);
        }

        return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Imports a file.</summary>
    /// <param name="path">The media file (any characters).</param>
    /// <param name="ct">Cancels the import.</param>
    /// <returns>The registered asset, its poster and an optional warning.</returns>
    /// <exception cref="FileNotFoundException">When the file does not exist.</exception>
    /// <exception cref="FfmpegException">When ffprobe cannot read the file (unsupported).</exception>
    /// <exception cref="InvalidDataException">When the file has neither a video nor an audio stream.</exception>
    public async Task<MediaImportResult> ImportAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var file = new FileInfo(full);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The media file does not exist.", full);
        }

        var probe = await _probe.ProbeAsync(full, ct).ConfigureAwait(false);
        var info = probe.Info;
        if (info.Video is null && info.Audio is null)
        {
            throw new InvalidDataException($"'{file.Name}' has no video or audio stream Cutlass can use.");
        }

        var fingerprint = _fingerprints is { } memo
            ? await memo.GetAsync(full, ct).ConfigureAwait(false)
            : await Task.Run(() => ComputeFingerprintAsync(full, ct), ct).ConfigureAwait(false);
        file.Refresh();
        var asset = new MediaAsset(_ids.NewId(IIdGenerator.MediaPrefix), full, null, fingerprint, file.Length, file.LastWriteTimeUtc, info);

        var indexTask = IndexAsync(asset, ct);
        var posterTask = PosterAsync(asset, ct);
        await Task.WhenAll(indexTask, posterTask).ConfigureAwait(false);
        var (index, indexWarning) = await indexTask.ConfigureAwait(false);
        var (poster, posterWarning) = await posterTask.ConfigureAwait(false);

        _catalog.Register(asset, index);
        string?[] warnings = [indexWarning, posterWarning];
        var warning = string.Join(" ", warnings.Where(w => w is not null));
        return new MediaImportResult(asset, poster, warning.Length == 0 ? null : warning);
    }

    private static async Task AppendRangeAsync(FileStream stream, IncrementalHash hash, byte[] buffer, long offset, long count, CancellationToken ct)
    {
        stream.Position = offset;
        while (count > 0)
        {
            var want = (int)Math.Min(buffer.Length, count);
            var read = await stream.ReadAsync(buffer.AsMemory(0, want), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("The file became shorter while it was fingerprinted.");
            }

            hash.AppendData(buffer, 0, read);
            count -= read;
        }
    }

    private async Task<(FrameIndex? Index, string? Warning)> IndexAsync(MediaAsset asset, CancellationToken ct)
    {
        if (asset.Info.Video is null || asset.Info.IsStill)
        {
            return (null, null);
        }

        try
        {
            var index = await _frameIndex.GetAsync(asset, ct).ConfigureAwait(false);
            return index is null
                ? (null, "No frame index could be built (VFR-unsafe): seeks assume a constant frame rate.")
                : (index, null);
        }
        catch (FfmpegException ex)
        {
            return (null, $"No frame index could be built (VFR-unsafe): {ex.Message}");
        }
    }

    private async Task<(string? Path, string? Warning)> PosterAsync(MediaAsset asset, CancellationToken ct)
    {
        try
        {
            return (await _posters.GetOrCreateAsync(asset, ct).ConfigureAwait(false), null);
        }
        catch (Exception ex) when (ex is FfmpegException or IOException or UnauthorizedAccessException)
        {
            return (null, $"No poster could be rendered: {ex.Message}");
        }
    }
}
