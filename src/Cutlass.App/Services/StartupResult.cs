using Cutlass.Compiler.Capabilities;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Settings;

namespace Cutlass.App.Services;

/// <summary>What the background startup found (TECH_BRIEF 5): settings, recent projects, ffmpeg and the cache size.</summary>
/// <param name="Settings">The loaded settings.</param>
/// <param name="RecentProjects">The recent projects, newest first.</param>
/// <param name="State">The ffmpeg state.</param>
/// <param name="StatusText">The status bar text, for example "ffmpeg N-126308, libavfilter 12".</param>
/// <param name="Detail">A longer explanation for tooltips and the welcome overlay, or null.</param>
/// <param name="Toolset">The validated ffmpeg, or null.</param>
/// <param name="Capabilities">Its capabilities, or null.</param>
/// <param name="CacheBytes">The bytes used under the cache root.</param>
public sealed record StartupResult(
    AppSettings Settings,
    IReadOnlyList<string> RecentProjects,
    FfmpegState State,
    string StatusText,
    string? Detail,
    FfmpegToolset? Toolset,
    FfmpegCapabilities? Capabilities,
    long CacheBytes);
