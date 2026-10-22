using Cutlass.Core.Model;
using Cutlass.Core.Serialization;
using Cutlass.Media.Cache;
using Cutlass.Media.Services;

namespace Cutlass.Media.Projects;

/// <summary>
/// Finds a project's media files on open and relinks missing ones (DESIGN 11). On open every asset is looked up by
/// its absolute path, then by its path relative to the project; a file found only relative to the project (the
/// project folder moved) is relinked automatically. A file whose size or modification time changed is "changed" and
/// re-probed; a file found nowhere is offline. The Relink dialog searches a folder tree by file name, then by name
/// without extension, then by size plus fingerprint (a renamed file), and <see cref="RefreshAsync"/> re-probes the
/// new file so the caller can replace the asset with <c>TimelineEdits.ImportMedia</c> (which trims clips to a shorter
/// source) in one undo step. Thread-safe; every member that touches files runs off the UI thread.
/// </summary>
public sealed class MediaRelinker
{
    /// <summary>
    /// How far a modification time may differ and still count as unchanged: copies to FAT and exFAT volumes keep
    /// only 2 s precision.
    /// </summary>
    public static readonly TimeSpan MtimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>The most files a folder search looks at (a whole drive would otherwise take minutes).</summary>
    public const int MaxSearchFiles = 200_000;

    /// <summary>The most same-size files fingerprinted per wanted asset in a folder search.</summary>
    public const int MaxFingerprintCandidates = 32;

    private readonly ProbeService? _probe;
    private readonly Fingerprinter? _fingerprints;

    /// <summary>Creates the relinker.</summary>
    /// <param name="probe">Re-probes relinked and changed files, or null while ffmpeg is unavailable (the recorded
    /// media info is kept then).</param>
    /// <param name="fingerprints">The fingerprint memo, or null to hash without one.</param>
    public MediaRelinker(ProbeService? probe, Fingerprinter? fingerprints)
    {
        _probe = probe;
        _fingerprints = fingerprints;
    }

    /// <summary>The file name of an asset path, whichever separator the path uses.</summary>
    /// <param name="assetPath">The asset path.</param>
    /// <returns>The file name.</returns>
    public static string FileNameOf(string assetPath)
    {
        ArgumentNullException.ThrowIfNull(assetPath);
        var slash = Math.Max(assetPath.LastIndexOf('/'), OperatingSystem.IsWindows() ? assetPath.LastIndexOf('\\') : -1);
        return assetPath[(slash + 1)..];
    }

    /// <summary>The link state of an asset given what the file system says about its file. Pure.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="facts">The file's size and modification time, or null when it does not exist.</param>
    /// <returns>Online, changed or offline.</returns>
    public static MediaLinkState Classify(MediaAsset asset, MediaFileFacts? facts)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (facts is null)
        {
            return MediaLinkState.Offline;
        }

        var mtimeDelta = (facts.MtimeUtc.ToUniversalTime() - asset.MtimeUtc.ToUniversalTime()).Duration();
        return facts.SizeBytes == asset.SizeBytes && mtimeDelta <= MtimeTolerance ? MediaLinkState.Online : MediaLinkState.Changed;
    }

    /// <summary>
    /// Checks every asset of a project (DESIGN 11): the absolute path first, then the path relative to the project.
    /// Assets found only relative to the project get that path. A path <paramref name="untrusted"/> flags (a remote
    /// volume other than the project's, see <see cref="MediaLocation"/>) is not looked up unless its asset is in
    /// <paramref name="confirmedIds"/>; an asset found nowhere else is then offline and listed in
    /// <see cref="RelinkScan.UnconfirmedIds"/>. Pure apart from the lookups.
    /// </summary>
    /// <param name="project">The loaded project.</param>
    /// <param name="projectPath">The project file, or null for an untitled project (absolute paths only).</param>
    /// <param name="facts">Looks a file up, null when it does not exist.</param>
    /// <param name="untrusted">True for a path that must not be touched unconfirmed, or null to trust every path.</param>
    /// <param name="confirmedIds">Media the user confirmed (their paths are looked up anyway), or null.</param>
    /// <returns>The scan.</returns>
    public static RelinkScan Scan(
        Project project,
        string? projectPath,
        Func<string, MediaFileFacts?> facts,
        Func<string, bool>? untrusted = null,
        IReadOnlyCollection<string>? confirmedIds = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(facts);
        var states = new Dictionary<string, MediaLinkState>(StringComparer.Ordinal);
        var moved = new List<string>();
        var unconfirmed = new List<string>();
        var media = project.Media.ToBuilder();
        for (var i = 0; i < media.Count; i++)
        {
            var asset = media[i];
            var candidates = ProjectSerializer.CandidateMediaPaths(asset, projectPath);
            var trustAll = untrusted is null || (confirmedIds?.Contains(asset.Id) ?? false);
            var foundIndex = -1;
            var skipped = false;
            MediaFileFacts? foundFacts = null;
            for (var c = 0; c < candidates.Length && foundIndex < 0; c++)
            {
                if (!trustAll && untrusted!(candidates[c]))
                {
                    skipped = true;
                    continue;
                }

                if (facts(candidates[c]) is { } f)
                {
                    foundIndex = c;
                    foundFacts = f;
                }
            }

            if (foundIndex < 0 && skipped)
            {
                unconfirmed.Add(asset.Id);
            }

            states[asset.Id] = Classify(asset, foundFacts);
            if (foundIndex > 0)
            {
                // Found relative to the project: the project folder moved together with its media.
                media[i] = asset with { Path = candidates[foundIndex] };
                moved.Add(asset.Id);
            }
        }

        var result = moved.Count == 0 ? project : project with { Media = media.ToImmutable() };
        return new RelinkScan(result, states, moved) { UnconfirmedIds = unconfirmed };
    }

    /// <summary>
    /// Runs <see cref="Scan"/> against the real file system, off the calling thread. Remote paths off the project's
    /// volume (<see cref="MediaLocation.IsUntrusted"/>) are not touched: they come back offline in
    /// <see cref="RelinkScan.UnconfirmedIds"/> until the user confirms them.
    /// </summary>
    /// <param name="project">The loaded project.</param>
    /// <param name="projectPath">The project file, or null for an untitled project.</param>
    /// <param name="cancellationToken">Cancels before the scan starts.</param>
    /// <returns>The scan.</returns>
    public static Task<RelinkScan> ScanAsync(Project project, string? projectPath, CancellationToken cancellationToken = default) =>
        ScanAsync(project, projectPath, [], cancellationToken);

    /// <summary>
    /// Runs <see cref="Scan"/> against the real file system, looking up the paths of the confirmed media even when
    /// they are remote (the user confirmed them in the relink flow).
    /// </summary>
    /// <param name="project">The loaded project.</param>
    /// <param name="projectPath">The project file, or null for an untitled project.</param>
    /// <param name="confirmedIds">The media ids the user confirmed.</param>
    /// <param name="cancellationToken">Cancels before the scan starts.</param>
    /// <returns>The scan.</returns>
    public static Task<RelinkScan> ScanAsync(
        Project project,
        string? projectPath,
        IReadOnlyCollection<string> confirmedIds,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Scan(project, projectPath, MediaFileFacts.Of, p => MediaLocation.IsUntrusted(p, projectPath), confirmedIds),
            cancellationToken);

    /// <summary>
    /// Matches wanted assets to files by name (the Relink dialog's folder search, DESIGN 11): the exact file name
    /// first, then the name without extension (a re-encoded file). Among several files with the name, one with the
    /// recorded size wins, then the shortest path. Each file is used once. Names compare case-insensitively only on
    /// Windows. Pure.
    /// </summary>
    /// <param name="wanted">The assets to find.</param>
    /// <param name="files">The files found in the folder tree.</param>
    /// <param name="sizeOf">The size of a file, or null when unknown; may be null.</param>
    /// <param name="ignoreCase">True to compare names case-insensitively (Windows).</param>
    /// <returns>The new path of each matched media id.</returns>
    public static IReadOnlyDictionary<string, string> MatchByName(
        IReadOnlyList<MediaAsset> wanted,
        IReadOnlyList<string> files,
        Func<string, long?>? sizeOf,
        bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentNullException.ThrowIfNull(files);
        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(comparer);
        var byName = files.GroupBy(f => Path.GetFileName(f), comparer).ToDictionary(g => g.Key, g => g.ToList(), comparer);
        var byStem = files.GroupBy(f => Path.GetFileNameWithoutExtension(f), comparer).ToDictionary(g => g.Key, g => g.ToList(), comparer);

        void Pass(Func<MediaAsset, string> key, Dictionary<string, List<string>> index)
        {
            foreach (var asset in wanted)
            {
                if (result.ContainsKey(asset.Id) || !index.TryGetValue(key(asset), out var candidates))
                {
                    continue;
                }

                var pick = candidates
                    .Where(c => !used.Contains(c))
                    .OrderBy(c => sizeOf?.Invoke(c) == asset.SizeBytes ? 0 : 1)
                    .ThenBy(c => c.Length)
                    .ThenBy(c => c, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (pick is not null)
                {
                    result[asset.Id] = pick;
                    used.Add(pick);
                }
            }
        }

        Pass(a => FileNameOf(a.Path), byName);
        Pass(a => Path.GetFileNameWithoutExtension(FileNameOf(a.Path)), byStem);
        return result;
    }

    /// <summary>Lists the files below a folder, recursively, skipping folders that cannot be read.</summary>
    /// <param name="folder">The folder.</param>
    /// <param name="max">The most files to list.</param>
    /// <returns>The full paths.</returns>
    public static IReadOnlyList<string> ListFiles(string folder, int max = MaxSearchFiles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        try
        {
            return [.. Directory.EnumerateFiles(folder, "*", options).Take(max)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Searches a folder tree for the wanted assets ("Search folder...", DESIGN 11): by name, by name without
    /// extension, then by size plus fingerprint for files that were renamed.
    /// </summary>
    /// <param name="wanted">The assets to find (normally every offline one).</param>
    /// <param name="folder">The folder to search, recursively.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The new path of each matched media id.</returns>
    public async Task<IReadOnlyDictionary<string, string>> SearchFolderAsync(
        IReadOnlyList<MediaAsset> wanted,
        string folder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wanted);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var files = await Task.Run(() => ListFiles(folder), cancellationToken).ConfigureAwait(false);
        var sizes = await Task.Run(
            () => files.ToDictionary(f => f, f => MediaFileFacts.Of(f)?.SizeBytes, StringComparer.Ordinal),
            cancellationToken).ConfigureAwait(false);
        var matches = new Dictionary<string, string>(
            MatchByName(wanted, files, f => sizes.GetValueOrDefault(f), OperatingSystem.IsWindows()),
            StringComparer.Ordinal);

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var used = new HashSet<string>(matches.Values, comparer);
        foreach (var asset in wanted.Where(a => !matches.ContainsKey(a.Id) && a.Fingerprint.Length > 0))
        {
            var sameSize = files.Where(f => !used.Contains(f) && sizes.GetValueOrDefault(f) == asset.SizeBytes).Take(MaxFingerprintCandidates);
            foreach (var candidate in sameSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fingerprint;
                try
                {
                    fingerprint = await FingerprintAsync(candidate, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (string.Equals(fingerprint, asset.Fingerprint, StringComparison.Ordinal))
                {
                    matches[asset.Id] = candidate;
                    used.Add(candidate);
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Re-reads a media file for an asset (relink or a changed file): probes it, fingerprints it and records its size
    /// and modification time. The caller replaces the asset with <c>TimelineEdits.ImportMedia</c>, which trims the
    /// clips to a shorter source and removes clips whose stream is gone.
    /// </summary>
    /// <param name="asset">The asset to update (its id is kept).</param>
    /// <param name="path">The file to link to.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The updated asset; the relative path is cleared (the next save computes it).</returns>
    /// <exception cref="FileNotFoundException">When the file does not exist.</exception>
    /// <exception cref="InvalidDataException">When the file has neither video nor audio.</exception>
    /// <exception cref="Ffmpeg.FfmpegException">When ffprobe cannot read the file.</exception>
    public async Task<MediaAsset> RefreshAsync(MediaAsset asset, string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var file = new FileInfo(full);
        if (!file.Exists)
        {
            throw new FileNotFoundException("The media file does not exist.", full);
        }

        if (Platform.FileIdentity.KindOf(full) != Platform.FileKind.Regular)
        {
            // A device or FIFO would block or never end when fingerprinted.
            throw new InvalidDataException($"'{file.Name}' is not a regular file.");
        }

        var info = asset.Info;
        if (_probe is not null)
        {
            info = (await _probe.ProbeAsync(full, cancellationToken).ConfigureAwait(false)).Info;
            if (info.Video is null && info.Audio is null)
            {
                throw new InvalidDataException($"'{file.Name}' has no video or audio stream Cutlass can use.");
            }
        }

        var fingerprint = await FingerprintAsync(full, cancellationToken).ConfigureAwait(false);
        file.Refresh();
        return asset with
        {
            Path = full,
            RelativePath = null,
            Fingerprint = fingerprint,
            SizeBytes = file.Length,
            MtimeUtc = file.LastWriteTimeUtc,
            Info = info,
        };
    }

    private Task<string> FingerprintAsync(string path, CancellationToken cancellationToken) =>
        _fingerprints is { } memo
            ? memo.GetAsync(path, cancellationToken)
            : Task.Run(() => MediaImporter.ComputeFingerprintAsync(path, cancellationToken), cancellationToken);
}
