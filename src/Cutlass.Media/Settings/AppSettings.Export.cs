namespace Cutlass.Media.Settings;

/// <summary>The export dialog defaults (DESIGN 9 and 10): the dialog remembers the last folder and preset here.</summary>
public sealed partial record AppSettings
{
    /// <summary>
    /// The folder the export dialog suggests, or null for the project's folder (else the Videos folder). The export
    /// dialog stores the folder of every queued export here, so it doubles as "the last export folder".
    /// </summary>
    public string? ExportFolder { get; set; }

    /// <summary>
    /// The id of the preset the export dialog selects first (<c>ExportPreset.Id</c>, for example <c>h264</c>). Updated
    /// by every queued export. An unknown or unavailable id falls back to H.264, then to the first available preset.
    /// </summary>
    public string ExportPreset { get; set; } = "h264";
}
