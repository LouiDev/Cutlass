using System.Collections.Concurrent;
using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;
using Cutlass.Media.Cache;
using Cutlass.Media.Ffmpeg;

namespace Cutlass.Media.Services;

/// <summary>
/// Builds and caches waveform peak pyramids (TECH_BRIEF 6.5): ffmpeg decodes the audio stream to 16 kHz mono s16le
/// on stdout (<c>-hide_banner -nostdin -v error -i &lt;file&gt; -map 0:&lt;a&gt; -vn -af aresample=async=1:first_pts=0
/// -ac 1 -ar 16000 -f s16le pipe:1</c>; the filter pads audio that starts after <c>format.start_time</c>, so peak 0 is
/// the format start, the origin of source time, and the waveform lines up with what is heard), C# takes min/max over
/// 160-sample blocks (100 peaks per second) and builds the halving levels. Progress is bytes read against
/// <c>duration * 32000</c>. The result is <c>peaks.bin</c> in the media cache. Thread-safe.
/// </summary>
public sealed class WaveformService
{
    /// <summary>Decoded bytes per second (16000 samples of 2 bytes).</summary>
    public const int BytesPerSecond = PeakPyramid.DecodeSampleRate * 2;

    /// <summary>How long one decode may take.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(60);

    private readonly ProcessRunner _runner;
    private readonly FfmpegToolset _toolset;
    private readonly WorkDirectory _work;
    private readonly MediaCache _cache;
    private readonly ConcurrentDictionary<string, PeakPyramid> _known = new(StringComparer.Ordinal);

    /// <summary>Creates the service.</summary>
    /// <param name="runner">Runs ffmpeg.</param>
    /// <param name="toolset">The validated ffmpeg.</param>
    /// <param name="work">The work directory ffmpeg runs in.</param>
    /// <param name="cache">The media cache.</param>
    public WaveformService(ProcessRunner runner, FfmpegToolset toolset, WorkDirectory work, MediaCache cache)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(work);
        ArgumentNullException.ThrowIfNull(cache);
        _runner = runner;
        _toolset = toolset;
        _work = work;
        _cache = cache;
    }

    /// <summary>The decode arguments (see the class summary).</summary>
    /// <param name="file">The media file (one argument, never quoted).</param>
    /// <param name="audioStreamIndex">The absolute audio stream index.</param>
    /// <returns>The arguments, without the executable.</returns>
    public static IReadOnlyList<string> BuildArguments(string file, int audioStreamIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        return
        [
            "-hide_banner", "-nostdin", "-v", "error", "-i", file, "-map", "0:" + FfNum.Integer(audioStreamIndex),
            "-vn", "-af", "aresample=async=1:first_pts=0",
            "-ac", "1", "-ar", FfNum.Integer(PeakPyramid.DecodeSampleRate), "-f", "s16le", "pipe:1",
        ];
    }

    /// <summary>The pyramid of an asset if it was built or loaded already. Memory only, so safe on the UI thread.</summary>
    /// <param name="asset">The asset.</param>
    /// <returns>The pyramid, or null.</returns>
    public PeakPyramid? TryGet(MediaAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _known.TryGetValue(asset.Fingerprint, out var pyramid) ? pyramid : null;
    }

    /// <summary>Returns the pyramid of an asset from the cache, decoding the audio first when missing.</summary>
    /// <param name="asset">The asset.</param>
    /// <param name="progress">Receives fractions from 0 to 1, or null.</param>
    /// <param name="cancellationToken">Cancels the decode.</param>
    /// <returns>The pyramid, or null for media without audio.</returns>
    /// <exception cref="FfmpegException">When ffmpeg fails.</exception>
    public async Task<PeakPyramid?> GetAsync(MediaAsset asset, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (asset.Info.Audio is not { } audio)
        {
            return null;
        }

        if (TryGet(asset) is { } known)
        {
            return known;
        }

        var fingerprint = asset.Fingerprint;
        var cached = await Task.Run(
            () => _cache.TryGetArtifact(fingerprint, CacheArtifacts.Peaks, CacheArtifacts.PeaksFile) is { } path ? PeakPyramid.ReadFile(path) : null,
            cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _known[fingerprint] = cached;
            return cached;
        }

        var pyramid = await DecodeAsync(asset.Path, audio.StreamIndex, asset.Info.DurationMicros, progress, cancellationToken).ConfigureAwait(false);
        var temp = _cache.NewTempFile(fingerprint, "peaks", ".bin");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                using var buffer = new MemoryStream();
                pyramid.WriteTo(buffer);
                await stream.WriteAsync(buffer.GetBuffer().AsMemory(0, (int)buffer.Length), cancellationToken).ConfigureAwait(false);
            }

            _cache.Commit(fingerprint, CacheArtifacts.Peaks, temp, CacheArtifacts.PeaksFile);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The startup cleanup removes abandoned temps.
            }
        }

        _known[fingerprint] = pyramid;
        progress?.Report(1);
        return pyramid;
    }

    private async Task<PeakPyramid> DecodeAsync(string file, int stream, long durationMicros, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        _work.EnsureCreated();
        var spec = new ProcessSpec(_toolset.FfmpegPath, BuildArguments(file, stream), _work.Path, RedirectStdin: false, ProcessPurpose.Background, BelowNormalPriority: true);
        await using var process = _runner.Start(spec);
        using var timeoutCts = new CancellationTokenSource(Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var expected = Math.Max(1.0, durationMicros / 1e6 * BytesPerSecond);
        var builder = new PeakPyramid.Builder();
        var buffer = new byte[1 << 16];
        var carry = 0;
        long total = 0;
        // A pipe read does not reliably observe a token on every OS, so cancellation kills the process, which ends
        // the read with EOF or an IOException.
        using (linked.Token.Register(() => _ = process.KillAsync()))
        {
            try
            {
                int read;
                while ((read = await process.Stdout.ReadAsync(buffer.AsMemory(carry), CancellationToken.None).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    var available = carry + read;
                    var even = available & ~1;
                    builder.AppendBytes(buffer.AsSpan(0, even));
                    carry = available - even;
                    if (carry > 0)
                    {
                        buffer[0] = buffer[even];
                    }

                    progress?.Report(Math.Min(1, total / expected));
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException && linked.IsCancellationRequested)
            {
                // The cancellation registration killed the process.
            }

            await process.Exited.ConfigureAwait(false);
        }

        if (linked.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new FfmpegException(
                $"The waveform decode did not finish within {Timeout.TotalMinutes:0} min and was stopped.",
                FfmpegException.NoExitCode,
                process.StderrTail(ProcessResult.ExceptionTailLines),
                process.DisplayCommand);
        }

        var exitCode = await process.Exited.ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new FfmpegException(exitCode, process.StderrTail(ProcessResult.ExceptionTailLines), process.DisplayCommand);
        }

        return builder.Build();
    }
}
