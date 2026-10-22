using System.Collections.Immutable;

namespace Cutlass.Compiler.Capabilities;

/// <summary>
/// What the installed ffmpeg can do, as detected by the capability probe (TECH_BRIEF 5.2): its build id, the
/// libavfilter version, the script syntax and the available filters and encoders.
/// </summary>
/// <param name="BuildId">The build id from <c>ffmpeg version (\S+)</c>, for example <c>N-126308-gd411d9e752-20260828</c>.</param>
/// <param name="AvfilterMajor">The libavfilter major version.</param>
/// <param name="AvfilterMinor">The libavfilter minor version.</param>
/// <param name="ScriptSyntax">How a filtergraph script file is passed.</param>
/// <param name="Filters">The names of every available filter (ordinal comparison).</param>
/// <param name="Encoders">The names of every available encoder (ordinal comparison).</param>
public sealed record FfmpegCapabilities(
    string BuildId,
    int AvfilterMajor,
    int AvfilterMinor,
    FilterScriptSyntax ScriptSyntax,
    ImmutableSortedSet<string> Filters,
    ImmutableSortedSet<string> Encoders)
{
    /// <summary>The oldest supported libavfilter major version (ffmpeg 6.0).</summary>
    public const int MinimumAvfilterMajor = 9;

    /// <summary>The filters preview and export cannot work without (TECH_BRIEF 5.2).</summary>
    public static ImmutableArray<string> RequiredFilters { get; } =
    [
        "color", "anullsrc", "nullsrc", "trim", "atrim", "setpts", "asetpts", "fps", "tpad", "apad", "concat", "xfade",
        "overlay", "scale", "pad", "crop", "setsar", "setparams", "format", "aformat", "aresample", "atempo", "afade",
        "fade", "volume", "amix", "amerge", "adelay", "eq", "lut", "framestep", "alimiter", "split", "palettegen",
        "paletteuse", "tile", "settb", "anull",
    ];

    /// <summary>The required filters this build lacks, in <see cref="RequiredFilters"/> order.</summary>
    public ImmutableArray<string> MissingRequiredFilters =>
        [.. RequiredFilters.Where(f => !Filters.Contains(f))];

    /// <summary>
    /// True when preview and export can run: libavfilter is at least <see cref="MinimumAvfilterMajor"/> and no
    /// required filter is missing. When false the ffmpeg status is red.
    /// </summary>
    public bool CanRender => AvfilterMajor >= MinimumAvfilterMajor && MissingRequiredFilters.IsEmpty;

    /// <summary>True when <c>drawtext</c> exists. Without it titles are disabled with a banner.</summary>
    public bool HasDrawtext => Filters.Contains("drawtext");

    /// <summary>
    /// The keyword for <c>-enc_time_base</c> that keeps the demuxer time base: <c>demux</c> when libavfilter is 10
    /// or newer, else <c>-1</c> (TECH_BRIEF 5.2).
    /// </summary>
    public string EncoderTimeBaseKeyword => AvfilterMajor >= 10 ? "demux" : "-1";

    /// <summary>
    /// The option that reads a filtergraph from a file: <c>-/filter_complex</c>, <c>-filter_complex_script</c>, or
    /// null when only inline graphs work.
    /// </summary>
    public string? ScriptOption => ScriptSyntax switch
    {
        FilterScriptSyntax.Slash => "-/filter_complex",
        FilterScriptSyntax.LegacyScript => "-filter_complex_script",
        _ => null,
    };

    /// <summary>Checks whether a filter is available.</summary>
    /// <param name="name">The filter name, for example <c>xfade</c>.</param>
    /// <returns>True when the build has it.</returns>
    public bool HasFilter(string name) => Filters.Contains(name);

    /// <summary>Checks whether an encoder is available.</summary>
    /// <param name="name">The encoder name, for example <c>libx264</c>.</param>
    /// <returns>True when the build has it.</returns>
    public bool HasEncoder(string name) => Encoders.Contains(name);
}
