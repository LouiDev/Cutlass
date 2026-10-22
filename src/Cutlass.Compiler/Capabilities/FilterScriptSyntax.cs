namespace Cutlass.Compiler.Capabilities;

/// <summary>How the installed ffmpeg accepts a filtergraph from a script file (TECH_BRIEF 5.2 and 7.7).</summary>
public enum FilterScriptSyntax
{
    /// <summary>No script option works: graphs are always passed inline and fail clearly above 30,000 characters.</summary>
    InlineOnly = 0,

    /// <summary><c>-/filter_complex &lt;file&gt;</c> (ffmpeg 7 and later, including master).</summary>
    Slash = 1,

    /// <summary><c>-filter_complex_script &lt;file&gt;</c> (ffmpeg 6.x, libavfilter 9).</summary>
    LegacyScript = 2,
}
