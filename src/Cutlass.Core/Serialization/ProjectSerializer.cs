using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cutlass.Core.Model;
using Cutlass.Core.Serialization.Dto;

namespace Cutlass.Core.Serialization;

/// <summary>
/// Reads and writes <c>.cutlass</c> project files and <c>.cutlass.user</c> sidecars (TECH_BRIEF 3.5, DESIGN 11).
/// The model maps to DTOs, so it carries no JSON attributes. Output is UTF-8 without BOM, camelCase, indented, with
/// <c>\n</c> line endings, invariant number formatting and forward-slash paths. Reading ignores unknown fields and
/// migrates older schemas. This class does no file I/O; <c>ProjectStore</c> writes atomically.
/// </summary>
public static partial class ProjectSerializer
{
    /// <summary>The value of the <c>format</c> field.</summary>
    public const string FormatName = "cutlass-project";

    /// <summary>The project file extension.</summary>
    public const string ProjectExtension = ".cutlass";

    /// <summary>The sidecar suffix appended to the project path.</summary>
    public const string SidecarSuffix = ".user";

    /// <summary>The autosave suffix appended to the project path.</summary>
    public const string AutosaveSuffix = ".autosave";

    /// <summary>
    /// The generated context with the relaxed encoder, so non-ASCII text and quotes stay readable instead of becoming
    /// <c>\u</c> escapes. The files are never embedded in HTML, so HTML-sensitive escaping is not needed.
    /// </summary>
    private static readonly ProjectJsonContext Json = new(new JsonSerializerOptions(ProjectJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>The <c>app</c> field: <c>"Cutlass &lt;major.minor.patch&gt;"</c> of this assembly.</summary>
    public static string AppName { get; } = "Cutlass " + (typeof(ProjectSerializer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    /// <summary>Serializes a project to JSON text (ending with a newline).</summary>
    /// <param name="project">The project.</param>
    /// <param name="projectFilePath">
    /// Where the file will be written; when given, each media <c>relativePath</c> is computed from it (null when the
    /// media is on another drive). When null, the assets' stored relative paths are written unchanged.
    /// </param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(Project project, string? projectFilePath = null) =>
        Utf8NoBom.GetString(SerializeToUtf8Bytes(project, projectFilePath));

    /// <summary>Serializes a project to UTF-8 bytes without BOM (ending with a newline).</summary>
    /// <param name="project">The project.</param>
    /// <param name="projectFilePath">See <see cref="Serialize"/>.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] SerializeToUtf8Bytes(Project project, string? projectFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var dto = ToDto(project, projectFilePath);
        var json = JsonSerializer.SerializeToUtf8Bytes(dto, Json.ProjectFileDto);
        var result = new byte[json.Length + 1];
        json.CopyTo(result, 0);
        result[^1] = (byte)'\n';
        return result;
    }

    /// <summary>Reads a project from JSON text, migrating older schemas.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The project, with <see cref="Project.SchemaVersion"/> set to the current version.</returns>
    /// <exception cref="InvalidDataException">When the text is not a readable Cutlass project.</exception>
    public static Project Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json, documentOptions: DocumentOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The project file is not valid JSON: {ex.Message}", ex);
        }

        return FromNode(root);
    }

    /// <summary>Reads a project from UTF-8 bytes (a BOM is tolerated), migrating older schemas.</summary>
    /// <param name="utf8Json">The UTF-8 bytes.</param>
    /// <returns>The project.</returns>
    /// <exception cref="InvalidDataException">When the bytes are not a readable Cutlass project.</exception>
    public static Project Deserialize(ReadOnlySpan<byte> utf8Json)
    {
        var bom = Encoding.UTF8.Preamble;
        if (utf8Json.StartsWith(bom))
        {
            utf8Json = utf8Json[bom.Length..];
        }

        string text;
        try
        {
            text = Utf8NoBom.GetString(utf8Json);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("The project file is not valid UTF-8.", ex);
        }

        return Deserialize(text);
    }

    /// <summary>Reads a project, reporting failure instead of throwing.</summary>
    /// <param name="json">The JSON text.</param>
    /// <param name="project">The project, or null.</param>
    /// <param name="error">The reason for failure, or null.</param>
    /// <returns>True when read.</returns>
    public static bool TryDeserialize(string json, out Project? project, out string? error)
    {
        try
        {
            project = Deserialize(json);
            error = null;
            return true;
        }
        catch (InvalidDataException ex)
        {
            project = null;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Serializes view state for the sidecar (ending with a newline).</summary>
    /// <param name="state">The state.</param>
    /// <returns>The JSON text.</returns>
    public static string SerializeViewState(ViewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var s = state.Sanitize();
        var dto = new ViewStateDto
        {
            PixelsPerFrame = s.PixelsPerFrame,
            ScrollFrame = s.ScrollFrame,
            ScrollY = s.ScrollY,
            Playhead = s.Playhead,
            TrackHeights = s.TrackHeights.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
            PreviewQuality = s.PreviewQuality,
            UseProxies = s.UseProxies,
            BinView = s.BinView,
        };
        return JsonSerializer.Serialize(dto, Json.ViewStateDto) + "\n";
    }

    /// <summary>
    /// Reads sidecar view state. A missing, damaged or partial sidecar never blocks opening a project: anything
    /// unreadable falls back to <see cref="ViewState.Default"/> values.
    /// </summary>
    /// <param name="json">The sidecar text, or null when there is none.</param>
    /// <returns>The sanitized state.</returns>
    public static ViewState DeserializeViewState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ViewState.Default;
        }

        try
        {
            var dto = JsonSerializer.Deserialize(json, Json.ViewStateDto);
            if (dto is null)
            {
                return ViewState.Default;
            }

            return new ViewState(
                dto.PixelsPerFrame,
                dto.ScrollFrame,
                dto.ScrollY,
                dto.Playhead,
                (dto.TrackHeights ?? []).ToImmutableDictionary(StringComparer.Ordinal),
                dto.PreviewQuality ?? ViewState.Default.PreviewQuality,
                dto.UseProxies,
                dto.BinView ?? ViewState.Default.BinView).Sanitize();
        }
        catch (JsonException)
        {
            return ViewState.Default;
        }
    }

    /// <summary>
    /// The places to look for a media file on open (DESIGN 11): its absolute path, then its path relative to the
    /// project file. Paths use the platform's separators.
    /// </summary>
    /// <param name="asset">The asset.</param>
    /// <param name="projectFilePath">The project file path, or null for an untitled project.</param>
    /// <returns>The candidate paths in lookup order, without duplicates.</returns>
    public static ImmutableArray<string> CandidateMediaPaths(MediaAsset asset, string? projectFilePath)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var candidates = new List<string> { ToNativePath(asset.Path) };
        if (asset.RelativePath is { Length: > 0 } relative && projectFilePath is { Length: > 0 }
            && Path.GetDirectoryName(Path.GetFullPath(projectFilePath)) is { } directory)
        {
            var resolved = Path.GetFullPath(Path.Combine(directory, ToNativePath(relative)));
            if (!candidates.Contains(resolved, StringComparer.Ordinal))
            {
                candidates.Add(resolved);
            }
        }

        return [.. candidates];
    }

    private static Project FromNode(JsonNode? root)
    {
        if (root is not JsonObject obj)
        {
            throw new InvalidDataException("A project file must contain a JSON object.");
        }

        if (obj["format"] is { } format && (format.GetValueKind() != JsonValueKind.String || format.GetValue<string>() != FormatName))
        {
            throw new InvalidDataException("This file is not a Cutlass project.");
        }

        ProjectMigrator.Migrate(obj);
        ProjectFileDto? dto;
        try
        {
            dto = obj.Deserialize(Json.ProjectFileDto);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The project file has an unexpected shape: {ex.Message}", ex);
        }

        if (dto is null)
        {
            throw new InvalidDataException("The project file is empty.");
        }

        return FromDto(dto);
    }

    private static string ToFilePath(string path) =>
        Path.DirectorySeparatorChar == '/' ? path : path.Replace(Path.DirectorySeparatorChar, '/');

    private static string ToNativePath(string path) =>
        Path.DirectorySeparatorChar == '/' ? path : path.Replace('/', Path.DirectorySeparatorChar);
}
