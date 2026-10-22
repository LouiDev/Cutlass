namespace Cutlass.Media.Ffmpeg;

/// <summary>
/// Everything needed to start one ffmpeg or ffprobe process (TECH_BRIEF 5.3). Arguments are passed one by one
/// through <see cref="System.Diagnostics.ProcessStartInfo.ArgumentList"/>, never joined into a command line.
/// </summary>
/// <remarks>
/// The caller puts <c>-nostdin</c> into <paramref name="Args"/> for every ffmpeg process except exports, whose
/// graceful stop is <c>q</c> on stdin. ffprobe never reads stdin and takes no such flag.
/// </remarks>
/// <param name="FileName">Full path of the executable.</param>
/// <param name="Args">The arguments, unquoted.</param>
/// <param name="WorkingDirectory">The working directory: the work directory, so graphs can use bare file names.</param>
/// <param name="RedirectStdin">True to open stdin for <see cref="RunningProcess.RequestQuitAsync"/> (exports).</param>
/// <param name="Purpose">Why the process runs.</param>
/// <param name="BelowNormalPriority">True to lower the process priority (background jobs).</param>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Args,
    string WorkingDirectory,
    bool RedirectStdin,
    ProcessPurpose Purpose,
    bool BelowNormalPriority);
