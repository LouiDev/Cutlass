using System.Collections.Immutable;
using System.Text.Json;
using Cutlass.Compiler.Capabilities;
using Cutlass.Media.Parsing;

namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// Detects what the installed ffmpeg can do (TECH_BRIEF 5.2): filters from <c>-filters</c>, encoders from
/// <c>-encoders</c> and the filter script syntax. Results are cached in <c>capabilities.json</c>, keyed by the
/// executable's path, size and modification time.
/// </summary>
public sealed class CapabilityProbe
{
    /// <summary>The script probe graph: one labelled output, which must be mapped (an unmapped graph always fails).</summary>
    public const string ProbeGraph = "nullsrc=s=16x16:d=0.04[v]";

    /// <summary>The bare name of the script probe file in the work directory.</summary>
    public const string ProbeFileName = "probe.ffg";

    /// <summary>How long each probe command may take.</summary>
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);

    private readonly ProcessRunner _runner;
    private readonly WorkDirectory _work;
    private readonly string? _cacheFile;

    /// <summary>Creates a probe.</summary>
    /// <param name="runner">Runs ffmpeg.</param>
    /// <param name="work">The work directory (the script probe file is written there).</param>
    /// <param name="cacheFile">The <c>capabilities.json</c> path, or null to never cache.</param>
    public CapabilityProbe(ProcessRunner runner, WorkDirectory work, string? cacheFile)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(work);
        _runner = runner;
        _work = work;
        _cacheFile = cacheFile;
    }

    /// <summary>The arguments of the script syntax probe for a given option.</summary>
    /// <param name="scriptOption"><c>-/filter_complex</c> or <c>-filter_complex_script</c>.</param>
    /// <returns>The arguments, without the executable.</returns>
    public static IReadOnlyList<string> ScriptProbeArguments(string scriptOption) =>
        ["-hide_banner", "-nostdin", "-v", "error", scriptOption, ProbeFileName, "-map", "[v]", "-f", "null", "-"];

    /// <summary>Returns the capabilities, from the cache when the executable is unchanged, else by probing.</summary>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <param name="forceRefresh">True to ignore the cache (Settings, "Re-detect").</param>
    /// <returns>The capabilities.</returns>
    /// <exception cref="FfmpegException">When <c>-filters</c> or <c>-encoders</c> fails.</exception>
    public async Task<FfmpegCapabilities> ProbeAsync(
        FfmpegToolset toolset,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(toolset);
        var (size, mtime) = FileKey(toolset.FfmpegPath);
        if (!forceRefresh && TryReadCache(toolset.FfmpegPath, size, mtime) is { } cached)
        {
            return cached;
        }

        _work.EnsureCreated();
        var filtersText = await RunTextAsync(toolset, ["-hide_banner", "-nostdin", "-filters"], cancellationToken).ConfigureAwait(false);
        var encodersText = await RunTextAsync(toolset, ["-hide_banner", "-nostdin", "-encoders"], cancellationToken).ConfigureAwait(false);
        var syntax = await ProbeScriptSyntaxAsync(toolset, cancellationToken).ConfigureAwait(false);

        var caps = new FfmpegCapabilities(
            toolset.Version.BuildId,
            toolset.Version.AvfilterMajor,
            toolset.Version.AvfilterMinor,
            syntax,
            ImmutableSortedSet.CreateRange(StringComparer.Ordinal, CapabilityListParser.ParseFilters(filtersText)),
            ImmutableSortedSet.CreateRange(StringComparer.Ordinal, CapabilityListParser.ParseEncoders(encodersText)));
        WriteCache(toolset.FfmpegPath, size, mtime, caps);
        return caps;
    }

    /// <summary>
    /// Detects the script syntax: <c>-/filter_complex</c> exiting 0 means Slash; otherwise, on libavfilter 9,
    /// <c>-filter_complex_script</c> exiting 0 means LegacyScript; otherwise InlineOnly.
    /// </summary>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="cancellationToken">Cancels the probe.</param>
    /// <returns>The syntax.</returns>
    public async Task<FilterScriptSyntax> ProbeScriptSyntaxAsync(FfmpegToolset toolset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(toolset);
        _work.WriteFile(ProbeFileName, ProbeGraph);

        if (await ExitsCleanAsync(toolset, "-/filter_complex", cancellationToken).ConfigureAwait(false))
        {
            return FilterScriptSyntax.Slash;
        }

        if (toolset.Version.AvfilterMajor == 9
            && await ExitsCleanAsync(toolset, "-filter_complex_script", cancellationToken).ConfigureAwait(false))
        {
            return FilterScriptSyntax.LegacyScript;
        }

        return FilterScriptSyntax.InlineOnly;
    }

    private static (long Size, long MtimeUtcTicks) FileKey(string path)
    {
        var info = new FileInfo(path);

        // Package managers install symlinks (WinGet Links, /usr/bin); the key follows them to the real binary.
        if (info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target && target.Exists)
        {
            info = target;
        }

        return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : (0, 0);
    }

    private async Task<bool> ExitsCleanAsync(FfmpegToolset toolset, string option, CancellationToken cancellationToken)
    {
        var spec = new ProcessSpec(toolset.FfmpegPath, ScriptProbeArguments(option), _work.Path, false, ProcessPurpose.Probe, false);
        try
        {
            var result = await _runner.RunAsync(spec, CommandTimeout, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0;
        }
        catch (FfmpegException)
        {
            return false;
        }
    }

    private async Task<string> RunTextAsync(FfmpegToolset toolset, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var spec = new ProcessSpec(toolset.FfmpegPath, args, _work.Path, false, ProcessPurpose.Probe, false);
        var result = await _runner.RunAsync(spec, CommandTimeout, cancellationToken).ConfigureAwait(false);
        return result.EnsureSuccess().StdoutText;
    }

    private FfmpegCapabilities? TryReadCache(string ffmpegPath, long size, long mtime)
    {
        if (_cacheFile is null || !File.Exists(_cacheFile))
        {
            return null;
        }

        try
        {
            var entry = JsonSerializer.Deserialize(File.ReadAllText(_cacheFile), MediaJsonContext.Default.CapabilityCacheEntry);
            if (entry is null
                || entry.Version != CapabilityCacheEntry.CurrentVersion
                || !string.Equals(entry.Path, ffmpegPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                || entry.Size != size
                || entry.MtimeUtcTicks != mtime)
            {
                return null;
            }

            return new FfmpegCapabilities(
                entry.BuildId,
                entry.AvfilterMajor,
                entry.AvfilterMinor,
                entry.ScriptSyntax,
                ImmutableSortedSet.CreateRange(StringComparer.Ordinal, entry.Filters),
                ImmutableSortedSet.CreateRange(StringComparer.Ordinal, entry.Encoders));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private void WriteCache(string ffmpegPath, long size, long mtime, FfmpegCapabilities caps)
    {
        if (_cacheFile is null)
        {
            return;
        }

        var entry = new CapabilityCacheEntry
        {
            Path = ffmpegPath,
            Size = size,
            MtimeUtcTicks = mtime,
            BuildId = caps.BuildId,
            AvfilterMajor = caps.AvfilterMajor,
            AvfilterMinor = caps.AvfilterMinor,
            ScriptSyntax = caps.ScriptSyntax,
            Filters = [.. caps.Filters],
            Encoders = [.. caps.Encoders],
        };

        var temp = _cacheFile + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFile)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(entry, MediaJsonContext.Default.CapabilityCacheEntry));
            File.Move(temp, _cacheFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is an optimisation; the next start probes again.
            try
            {
                File.Delete(temp);
            }
            catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
            {
                // Removed by the startup cleanup.
            }
        }
    }
}
