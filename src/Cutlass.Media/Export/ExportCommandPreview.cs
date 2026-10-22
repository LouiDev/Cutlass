using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Capabilities;
using Cutlass.Compiler.Emit;
using Cutlass.Compiler.Export;
using Cutlass.Media.Ffmpeg;
using Cutlass.Media.Platform;

namespace Cutlass.Media.Export;

/// <summary>
/// The export dialog's "Show ffmpeg command" (DESIGN 9): the command <see cref="ExportService"/> would run for a
/// request, built along the same path (title fonts resolved through <see cref="ExportFontResolver"/>, the same compile,
/// the same content-addressed script name and arguments). Nothing is started; only the bundled fonts and resolved user
/// fonts are copied into the work directory, as the export would do first.
/// </summary>
public static class ExportCommandPreview
{
    /// <summary>Builds the display command of an export.</summary>
    /// <param name="request">The export request.</param>
    /// <param name="toolset">The ffmpeg that would run.</param>
    /// <param name="capabilities">What the build can do.</param>
    /// <param name="fonts">Resolves title fonts.</param>
    /// <param name="cancellationToken">Cancels font resolution.</param>
    /// <returns>The command line for display (POSIX quoting on Linux, double quotes on Windows).</returns>
    /// <exception cref="InvalidOperationException">When the preset is not available on this build.</exception>
    public static async Task<string> BuildAsync(
        ExportRequest request,
        FfmpegToolset toolset,
        FfmpegCapabilities capabilities,
        FontLocator fonts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(toolset);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(fonts);
        var encoding = request.Preset.Resolve(capabilities)
            ?? throw new InvalidOperationException(request.Preset.UnavailableReason(capabilities));
        var (catalog, _) = await ExportFontResolver.ResolveAsync(request, fonts, cancellationToken).ConfigureAwait(false);
        var render = TimelineCompiler.Compile(request.Project, catalog, request.ToRenderRequest());
        return Format(render, encoding, request, capabilities, toolset.FfmpegPath);
    }

    /// <summary>Formats the export command of a compiled render (no font resolution; tests and catalogs without titles).</summary>
    /// <param name="render">The compiled render.</param>
    /// <param name="encoding">The resolved encoders.</param>
    /// <param name="request">The request (quality, size, rate and paths).</param>
    /// <param name="capabilities">The script syntax.</param>
    /// <param name="ffmpegPath">The ffmpeg executable.</param>
    /// <returns>The display command.</returns>
    public static string Format(
        CompiledRender render,
        ExportEncoding encoding,
        ExportRequest request,
        FfmpegCapabilities capabilities,
        string ffmpegPath)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capabilities);
        string? script = null;
        if (FfmpegArgsBuilder.UseScriptFile(RenderPurpose.Export, render.GraphText, capabilities.ScriptSyntax))
        {
            // The same content-addressed name WorkDirectory.WriteScript gives the graph when the export runs.
            script = "g_" + WorkDirectory.ShortHash(new UTF8Encoding(false).GetBytes(render.GraphText), 12) + ".ffg";
        }

        var graph = FfmpegArgsBuilder.Graph(render.GraphText, script, capabilities.ScriptSyntax);
        var args = FfmpegArgsBuilder.Export(
            render, encoding, request.Quality, request.Project.Sequence.Settings.Rate, request.Width, request.Height, graph, request.PartialPath);
        return ProcessRunner.FormatDisplayCommand(ffmpegPath, args, posix: !OperatingSystem.IsWindows());
    }
}
