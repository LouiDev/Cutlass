using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Core.Editing;
using Cutlass.Core.Model;

namespace Cutlass.Media.Export;

/// <summary>Everything one export needs (DESIGN 9). The project is an immutable snapshot taken when queued.</summary>
/// <param name="Project">The project snapshot.</param>
/// <param name="Catalog">The media the project uses.</param>
/// <param name="Preset">The export preset.</param>
/// <param name="Quality">The quality choice (ignored by fixed-quality presets).</param>
/// <param name="Width">The output width (even). GIF: the chosen width.</param>
/// <param name="Height">The output height (even).</param>
/// <param name="StartFrame">The first timeline frame (Whole: 0, In to Out: In).</param>
/// <param name="EndFrame">The end frame, exclusive (Whole: the sequence duration, In to Out: Out).</param>
/// <param name="LimitPeaks">True to run the mix through the peak limiter (on by default).</param>
/// <param name="OutputPath">The final output path. The extension follows the preset; one is added when missing.</param>
/// <param name="Progress">Receives throttled progress reports, or null.</param>
public sealed record ExportRequest(
    Project Project,
    IMediaCatalog Catalog,
    ExportPreset Preset,
    ExportQuality Quality,
    int Width,
    int Height,
    long StartFrame,
    long EndFrame,
    bool LimitPeaks,
    string OutputPath,
    IProgress<ExportProgress>? Progress = null)
{
    /// <summary>The Whole range: from frame 0 to the end of the last clip.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The range.</returns>
    public static (long Start, long End) WholeRange(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return (0, TimelineQuery.Duration(project));
    }

    /// <summary>The In to Out range, falling back to the start and the end of the sequence for a missing mark.</summary>
    /// <param name="project">The project.</param>
    /// <returns>The range.</returns>
    public static (long Start, long End) InOutRange(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var sequence = project.Sequence;
        return (sequence.InFrame ?? 0, sequence.OutFrame ?? TimelineQuery.Duration(project));
    }

    /// <summary>The final output path with the preset extension added when the path has none.</summary>
    public string FinalPath =>
        Path.GetFullPath(string.IsNullOrEmpty(Path.GetExtension(OutputPath)) ? OutputPath + Preset.Extension : OutputPath);

    /// <summary>
    /// The partial path ffmpeg writes to: <c>&lt;dir&gt;/&lt;name&gt;.partial.&lt;ext&gt;</c>. When a file of that name
    /// already exists, the job writes <c>&lt;name&gt;.partial-&lt;8 hex&gt;.&lt;ext&gt;</c> instead
    /// (<see cref="PartialPathFor"/>), so an export never deletes a file it did not write.
    /// </summary>
    public string PartialPath
    {
        get
        {
            var final = FinalPath;
            var directory = Path.GetDirectoryName(final) ?? string.Empty;
            return Path.Combine(directory, Path.GetFileNameWithoutExtension(final) + ".partial" + Path.GetExtension(final));
        }
    }

    /// <summary>The partial path with a given suffix.</summary>
    /// <param name="suffix">The suffix.</param>
    /// <returns><c>&lt;dir&gt;/&lt;name&gt;.partial-&lt;suffix&gt;.&lt;ext&gt;</c>.</returns>
    public string PartialPathFor(string suffix)
    {
        var final = FinalPath;
        var directory = Path.GetDirectoryName(final) ?? string.Empty;
        return Path.Combine(directory, Path.GetFileNameWithoutExtension(final) + ".partial-" + suffix + Path.GetExtension(final));
    }

    /// <summary>The compiler request for this export (Purpose Export, mixed audio, no proxies).</summary>
    /// <returns>The render request.</returns>
    public RenderRequest ToRenderRequest() => new(
        StartFrame,
        EndFrame,
        Width,
        Height,
        RenderPurpose.Export,
        IncludeVideo: Preset.HasVideo,
        IncludeAudio: Preset.HasAudio,
        UseProxies: false,
        AudioLayout.Mixed,
        FrameStep: 1,
        Preset.Tail,
        LimitPeaks);
}
