using Cutlass.Compiler.Graph;
using Cutlass.Core.Model;
using Cutlass.Core.Time;

namespace Cutlass.Compiler.Emit;

/// <summary>The state shared by the emitters during one compile.</summary>
internal sealed class EmitContext
{
    private readonly Dictionary<string, MediaRenderInfo?> _media = new(StringComparer.Ordinal);
    private readonly List<AuxFile> _auxFiles = [];
    private readonly HashSet<string> _auxNames = new(StringComparer.Ordinal);
    private readonly List<string> _warnings = [];
    private readonly HashSet<string> _warningSet = new(StringComparer.Ordinal);

    /// <summary>Creates the context.</summary>
    /// <param name="project">The project snapshot.</param>
    /// <param name="catalog">The media catalog.</param>
    /// <param name="request">The request.</param>
    public EmitContext(Project project, IMediaCatalog catalog, RenderRequest request)
    {
        Project = project;
        Catalog = catalog;
        Request = request;
        Rate = project.Sequence.Settings.Rate;
        Width = request.CanvasWidth;
        Height = request.CanvasHeight;
        PixelScale = (double)request.CanvasWidth / Math.Max(1, project.Sequence.Settings.Width);
        WindowSamples = TimeMath.SamplesAt(request.EndFrame, Rate) - TimeMath.SamplesAt(request.StartFrame, Rate);
    }

    /// <summary>The project.</summary>
    public Project Project { get; }

    /// <summary>The catalog.</summary>
    public IMediaCatalog Catalog { get; }

    /// <summary>The request.</summary>
    public RenderRequest Request { get; }

    /// <summary>The sequence rate <c>R</c>.</summary>
    public FrameRate Rate { get; }

    /// <summary>The canvas width <c>W</c>.</summary>
    public int Width { get; }

    /// <summary>The canvas height <c>H</c>.</summary>
    public int Height { get; }

    /// <summary>The preview scale <c>k = CanvasWidth / sequence width</c> for pixel quantities.</summary>
    public double PixelScale { get; }

    /// <summary>The window length in frames, <c>N</c>.</summary>
    public long WindowFrames => Request.FrameCount;

    /// <summary>The window length in samples, <c>S = SamplesAt(EndFrame) - SamplesAt(StartFrame)</c>.</summary>
    public long WindowSamples { get; }

    /// <summary>The scale flags: bicubic for export, bilinear for previews.</summary>
    public string ScaleFlags => Request.Purpose == RenderPurpose.Export ? "bicubic" : "bilinear";

    /// <summary>The graph under construction.</summary>
    public FilterGraphBuilder Graph { get; } = new();

    /// <summary>The inputs.</summary>
    public InputTable Inputs { get; } = new();

    /// <summary>The aux files, in first-use order.</summary>
    public IReadOnlyList<AuxFile> AuxFiles => _auxFiles;

    /// <summary>The warnings, in first-occurrence order.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>The canvas size as <c>WxH</c>.</summary>
    public string CanvasSize => FfNum.Integer(Width) + "x" + FfNum.Integer(Height);

    /// <summary>Looks up media once per compile.</summary>
    /// <param name="mediaId">The media id.</param>
    /// <returns>The render info, or null when offline.</returns>
    public MediaRenderInfo? Media(string mediaId)
    {
        if (!_media.TryGetValue(mediaId, out var info))
        {
            info = Catalog.Get(mediaId);
            _media[mediaId] = info;
        }

        return info;
    }

    /// <summary>Records a warning once.</summary>
    /// <param name="message">The message.</param>
    public void Warn(string message)
    {
        if (_warningSet.Add(message))
        {
            _warnings.Add(message);
        }
    }

    /// <summary>Records an aux file once (names are content-addressed).</summary>
    /// <param name="file">The file.</param>
    public void AddAuxFile(AuxFile file)
    {
        FilterEscaper.RequireBareName(file.FileName);
        if (_auxNames.Add(file.FileName))
        {
            _auxFiles.Add(file);
        }
    }

    /// <summary>Scales a pixel quantity by <see cref="PixelScale"/> and rounds it (halves away from zero).</summary>
    /// <param name="pixels">The quantity in sequence pixels.</param>
    /// <returns>The rounded canvas pixels.</returns>
    public long ScalePixels(double pixels) => (long)Math.Round(pixels * PixelScale, MidpointRounding.AwayFromZero);

    /// <summary>Seconds of a frame count at the sequence rate, floored to the microsecond.</summary>
    /// <param name="frames">The frames.</param>
    /// <returns>The text, for example <c>4.500000</c>.</returns>
    public string Seconds(long frames) => FfNum.Seconds(TimeMath.SecondsAt(frames, Rate));

    /// <summary>The audio sample index of a timeline frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns><c>SamplesAt(frame)</c>.</returns>
    public long SamplesAt(long frame) => TimeMath.SamplesAt(frame, Rate);
}
