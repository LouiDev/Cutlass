using System.Globalization;

namespace Cutlass.IntegrationTests.Infrastructure;

/// <summary>
/// Generates the lavfi test media of TECH_BRIEF 11.2 once per machine and recipe version into
/// <c>%TEMP%/cutlass-test-media-v&lt;n&gt;/</c>. Files are written under a temporary name and renamed, so parallel
/// runs never see partial files, and existing files are reused. Does nothing when ffmpeg is missing (the tests that
/// need it skip through <see cref="FfmpegFactAttribute"/>).
/// </summary>
public sealed class TestMediaFixture : IAsyncLifetime
{
    /// <summary>Bump when a recipe changes so stale media in the temp folder is not reused.</summary>
    public const int RecipeVersion = 1;

    /// <summary>Oracle frame width in pixels.</summary>
    public const int OracleWidth = 320;

    /// <summary>Oracle frame height in pixels.</summary>
    public const int OracleHeight = 180;

    /// <summary>Oracle clip length in seconds.</summary>
    public const int OracleSeconds = 20;

    /// <summary>Frames in the 25 fps oracles (20 s).</summary>
    public const int Oracle25FrameCount = 25 * OracleSeconds;

    private static readonly TimeSpan RecipeTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Creates the fixture; generation happens in <see cref="InitializeAsync"/>.</summary>
    public TestMediaFixture()
    {
        Directory = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"cutlass-test-media-v{RecipeVersion}"));
    }

    /// <summary>The folder holding the generated media.</summary>
    public string Directory { get; }

    /// <summary>The video encoder used for <see cref="Oracle25LongGop"/>: <c>libx264</c>, or <c>mpeg4</c> when the build lacks it.</summary>
    public string? LongGopEncoder { get; private set; }

    /// <summary>25 fps bit-block oracle, intra-only (ffv1), 500 frames.</summary>
    public string Oracle25Intra => PathOf("oracle25_intra.mkv");

    /// <summary>25 fps bit-block oracle with a 250 frame GOP and B-frames.</summary>
    public string Oracle25LongGop => PathOf("oracle25_lgop.mp4");

    /// <summary>29.97 fps bit-block oracle (ffv1).</summary>
    public string Oracle2997 => PathOf("oracle2997.mkv");

    /// <summary>Variable frame rate oracle: frame N at <c>N/25 + 0.01*mod(N,3)</c> seconds (ffv1).</summary>
    public string OracleVfr => PathOf("oracle_vfr.mkv");

    /// <summary>20 s of 48 kHz audio with a 0.9 impulse at every whole second.</summary>
    public string Impulse => PathOf("impulse.wav");

    /// <summary>3 s testsrc2 video at 30 fps with the impulse audio starting 0.5 s late (pcm_s16le in mov).</summary>
    public string DelayedAudio => PathOf("delayed_audio.mov");

    /// <summary>10 s at 30 fps: a white flash on frames with <c>mod(N,30)=0</c> and an impulse every second.</summary>
    public string Sync => PathOf("sync.mkv");

    /// <summary>10 s 1 kHz sine at 48 kHz.</summary>
    public string Tone => PathOf("tone.wav");

    /// <summary>A single 320x240 red PNG.</summary>
    public string RedPng => PathOf("red.png");

    /// <summary>4 s testsrc2 at 25 fps (mpeg4) with a 48 kHz stereo sine (aac) in mp4. CFR, start time 0.</summary>
    public string Cfr25Av => PathOf("cfr25_av.mp4");

    /// <summary>3 s testsrc2 at 30000/1001 fps (mpeg4) with a 44.1 kHz mono sine (pcm_s16le) in mkv.</summary>
    public string Cfr2997Av => PathOf("cfr2997_av.mkv");

    /// <summary>2 s of 25 fps video and 48 kHz audio in mov whose timestamps start at 2.5 s (<c>format.start_time</c>).</summary>
    public string OffsetStart => PathOf("offset_start.mov");

    /// <summary>
    /// Truly variable frame rate mp4: 30 frames at 30 fps, then 60 frames at 10 fps (90 frames, 7 s), so
    /// <c>r_frame_rate</c> (30) and <c>avg_frame_rate</c> (about 12.9) differ.
    /// </summary>
    public string VfrMixed => PathOf("vfr_mixed.mp4");

    /// <summary>The unrotated source of <see cref="Rotated90"/>: 320x180 at 30 fps with aac audio.</summary>
    public string RotatedSource => PathOf("rotated_src.mp4");

    /// <summary><see cref="RotatedSource"/> remuxed with <c>-display_rotation 90</c> (display matrix side data).</summary>
    public string Rotated90 => PathOf("rotated90.mp4");

    /// <summary>A folder whose name has spaces, an apostrophe, a comma, brackets and non-ASCII characters.</summary>
    public string OddDirectory => PathOf("it's, [odd] Ä 日本");

    /// <summary>
    /// 12 s of mono 48 kHz tone bursts: 100 ms (4800 samples) of 997 Hz under a Hann window, one every 24011 samples from
    /// sample 48000 (TECH_BRIEF 7.3, the atempo calibration signal).
    /// </summary>
    public string Bursts => PathOf("bursts.wav");

    /// <summary>The 25 fps oracle in AVI as mpeg4 with two B-frames: packets without pts that need the dts fallback.</summary>
    public string Oracle25AviBFrames => PathOf("oracle25_bf.avi");

    /// <summary>A copy of <see cref="Cfr25Av"/> inside <see cref="OddDirectory"/> under an equally odd name.</summary>
    public string OddPathClip => Path.Combine(OddDirectory, "clip ü [1] it's.mp4");

    /// <summary>Every generated file.</summary>
    public IReadOnlyList<string> AllFiles =>
    [
        Oracle25Intra, Oracle25LongGop, Oracle2997, OracleVfr, Impulse, DelayedAudio, Sync, Tone, RedPng,
        Cfr25Av, Cfr2997Av, OffsetStart, VfrMixed, RotatedSource, Rotated90, OddPathClip, Bursts, Oracle25AviBFrames,
    ];

    /// <summary>The spacing of the tone bursts in <see cref="Bursts"/>, in samples.</summary>
    public const int BurstSpacing = 24011;

    /// <summary>The length of one tone burst in <see cref="Bursts"/>, in samples.</summary>
    public const int BurstLength = 4800;

    /// <summary>The source sample of the first burst's start in <see cref="Bursts"/>.</summary>
    public const int FirstBurst = 48000;

    /// <summary>The value the oracle encodes in frame <paramref name="frameIndex"/> (16 bits, one per block).</summary>
    /// <param name="frameIndex">The zero-based decoded frame index.</param>
    /// <returns>The expected oracle value.</returns>
    public static int ExpectedOracleValue(long frameIndex) => (int)(frameIndex & 0xFFFF);

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (!TestFfmpeg.IsAvailable)
        {
            return;
        }

        System.IO.Directory.CreateDirectory(Directory);

        await GenerateAsync(Oracle25Intra, ["-f", "lavfi", "-i", OracleSource("25"), "-c:v", "ffv1"]).ConfigureAwait(false);
        await GenerateLongGopAsync().ConfigureAwait(false);
        await GenerateAsync(Oracle2997, ["-f", "lavfi", "-i", OracleSource("30000/1001"), "-c:v", "ffv1"]).ConfigureAwait(false);

        // Deviation from the brief's one-liner: setpts alone cannot express 10 ms offsets in the 1/25 lavfi time
        // base, and the encoder time base defaults to 1/25 too, so both are raised to 1/1000 (TECH_BRIEF 11.2 updated).
        await GenerateAsync(
            OracleVfr,
            [
                "-f", "lavfi", "-i", OracleSource("25"),
                "-vf", "settb=1/1000,setpts='(N/25+0.01*mod(N\\,3))/TB'",
                "-fps_mode", "vfr", "-enc_time_base", "1/1000", "-c:v", "ffv1",
            ]).ConfigureAwait(false);

        await GenerateAsync(Impulse, ["-f", "lavfi", "-i", ImpulseSource(OracleSeconds), "-c:a", "pcm_s16le"]).ConfigureAwait(false);

        await GenerateAsync(
            DelayedAudio,
            [
                "-f", "lavfi", "-i", "testsrc2=s=320x180:r=30:d=3",
                "-itsoffset", "0.5", "-f", "lavfi", "-i", ImpulseSource(3),
                "-map", "0:v", "-map", "1:a", "-c:v", "mpeg4", "-q:v", "2", "-c:a", "pcm_s16le",
            ]).ConfigureAwait(false);

        await GenerateAsync(
            Sync,
            [
                "-f", "lavfi", "-i",
                "color=c=black:s=320x180:r=30:d=10,geq=lum='if(eq(mod(N\\,30)\\,0)\\,235\\,16)':cb=128:cr=128,format=yuv420p",
                "-f", "lavfi", "-i", ImpulseSource(10),
                "-map", "0:v", "-map", "1:a", "-c:v", "ffv1", "-c:a", "pcm_s16le",
            ]).ConfigureAwait(false);

        await GenerateAsync(Tone, ["-f", "lavfi", "-i", "sine=f=1000:r=48000:d=10", "-c:a", "pcm_s16le"]).ConfigureAwait(false);
        await GenerateAsync(RedPng, ["-f", "lavfi", "-i", "color=c=red:s=320x240", "-frames:v", "1"]).ConfigureAwait(false);

        // M2 probe and process media.
        await GenerateAsync(
            Cfr25Av,
            [
                "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=4",
                "-f", "lavfi", "-i", "sine=f=440:r=48000:d=4",
                "-map", "0:v", "-map", "1:a", "-c:v", "mpeg4", "-q:v", "3", "-c:a", "aac", "-ac", "2", "-ar", "48000",
            ]).ConfigureAwait(false);
        await GenerateAsync(
            Cfr2997Av,
            [
                "-f", "lavfi", "-i", "testsrc2=s=320x180:r=30000/1001:d=3",
                "-f", "lavfi", "-i", "sine=f=440:r=44100:d=3",
                "-map", "0:v", "-map", "1:a", "-c:v", "mpeg4", "-q:v", "3", "-c:a", "pcm_s16le", "-ac", "1",
            ]).ConfigureAwait(false);
        await GenerateAsync(
            OffsetStart,
            [
                "-f", "lavfi", "-i", "testsrc2=s=320x180:r=25:d=2",
                "-f", "lavfi", "-i", "sine=f=440:r=48000:d=2",
                "-map", "0:v", "-map", "1:a", "-c:v", "mpeg4", "-q:v", "3", "-c:a", "pcm_s16le",
                "-output_ts_offset", "2.5",
            ]).ConfigureAwait(false);
        await GenerateAsync(
            VfrMixed,
            [
                "-f", "lavfi", "-i", "testsrc2=s=320x180:r=30:d=3",
                "-vf", "settb=1/1000,setpts='if(lt(N\\,30)\\,N/30\\,1+(N-30)/10)/TB'",
                "-fps_mode", "vfr", "-enc_time_base", "1/1000", "-video_track_timescale", "1000",
                "-c:v", "mpeg4", "-q:v", "5",
            ]).ConfigureAwait(false);
        await GenerateAsync(
            RotatedSource,
            [
                "-f", "lavfi", "-i", "testsrc2=s=320x180:r=30:d=2",
                "-f", "lavfi", "-i", "sine=f=440:r=48000:d=2",
                "-map", "0:v", "-map", "1:a", "-c:v", "mpeg4", "-q:v", "3", "-c:a", "aac",
            ]).ConfigureAwait(false);

        // -metadata:s:v rotate=90 no longer writes a display matrix on current ffmpeg; -display_rotation (an input
        // option since ffmpeg 6.0) does, and a stream copy keeps it.
        await GenerateAsync(Rotated90, ["-display_rotation", "90", "-i", RotatedSource, "-c", "copy"]).ConfigureAwait(false);

        await GenerateAsync(
            Bursts,
            [
                "-f", "lavfi", "-i",
                "aevalsrc='if(lt(mod(n-48000\\,24011)\\,4800)*gte(n\\,48000)\\,0.8*sin(2*PI*997*t)*(0.5-0.5*cos(2*PI*mod(n-48000\\,24011)/4800))\\,0)':s=48000:d=12",
                "-c:a", "pcm_s16le",
            ]).ConfigureAwait(false);
        await GenerateAsync(
            Oracle25AviBFrames,
            ["-f", "lavfi", "-i", OracleSource("25"), "-c:v", "mpeg4", "-bf", "2", "-q:v", "2"]).ConfigureAwait(false);

        System.IO.Directory.CreateDirectory(OddDirectory);
        if (!File.Exists(OddPathClip))
        {
            var temp = OddPathClip + ".tmp-" + Guid.NewGuid().ToString("N");
            File.Copy(Cfr25Av, temp);
            File.Move(temp, OddPathClip, overwrite: true);
        }
    }

    /// <inheritdoc />
    /// <remarks>The media is kept in the temp folder on purpose, so later runs reuse it.</remarks>
    public Task DisposeAsync() => Task.CompletedTask;

    private static string OracleSource(string rate) =>
        "color=c=black:s=320x180:r=" + rate + ":d=" + OracleSeconds.ToString(CultureInfo.InvariantCulture)
        + ",geq=lum='if(bitand(N\\,pow(2\\,floor(X/40)+8*floor(Y/90)))\\,235\\,16)':cb=128:cr=128,format=yuv420p";

    private static string ImpulseSource(int seconds) =>
        "aevalsrc='if(eq(mod(n\\,48000)\\,0)\\,0.9\\,0)':s=48000:d=" + seconds.ToString(CultureInfo.InvariantCulture);

    private string PathOf(string name) => Path.Combine(Directory, name);

    private async Task GenerateLongGopAsync()
    {
        var x264 = await TryGenerateAsync(
            Oracle25LongGop,
            ["-f", "lavfi", "-i", OracleSource("25"), "-c:v", "libx264", "-g", "250", "-bf", "3", "-crf", "18", "-pix_fmt", "yuv420p"])
            .ConfigureAwait(false);
        if (x264)
        {
            LongGopEncoder = "libx264";
            return;
        }

        await GenerateAsync(
            Oracle25LongGop,
            ["-f", "lavfi", "-i", OracleSource("25"), "-c:v", "mpeg4", "-g", "250", "-bf", "2", "-q:v", "2"]).ConfigureAwait(false);
        LongGopEncoder = "mpeg4";
    }

    private async Task GenerateAsync(string output, IReadOnlyList<string> args)
    {
        if (!await TryGenerateAsync(output, args).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"ffmpeg failed to generate {Path.GetFileName(output)}.");
        }
    }

    private async Task<bool> TryGenerateAsync(string output, IReadOnlyList<string> args)
    {
        if (File.Exists(output) && new FileInfo(output).Length > 0)
        {
            // Reused from an earlier run. The long-GOP encoder is then recorded from the file's codec.
            if (output == Oracle25LongGop)
            {
                LongGopEncoder ??= await ProbeCodecAsync(output).ConfigureAwait(false) == "h264" ? "libx264" : "mpeg4";
            }

            return true;
        }

        var temp = Path.Combine(
            Directory,
            Path.GetFileNameWithoutExtension(output) + ".tmp-" + Guid.NewGuid().ToString("N") + Path.GetExtension(output));
        List<string> fullArgs = ["-hide_banner", "-nostdin", "-v", "error", "-y", .. args, temp];
        try
        {
            var result = await TestProcess.RunAsync(TestFfmpeg.FfmpegPath, fullArgs, Directory, RecipeTimeout)
                .ConfigureAwait(false);
            if (result.ExitCode != 0 || !File.Exists(temp))
            {
                return false;
            }

            File.Move(temp, output, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private async Task<string> ProbeCodecAsync(string file)
    {
        var result = await TestProcess.RunAsync(
            TestFfmpeg.FfprobePath,
            ["-v", "error", "-select_streams", "v:0", "-show_entries", "stream=codec_name", "-of", "csv=p=0", file],
            Directory,
            TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return result.Stdout.Trim();
    }
}
