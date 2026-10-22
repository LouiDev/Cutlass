namespace Cutlass.Compiler.Export;

/// <summary>The encoders a preset resolved to on one ffmpeg build (primary choice or fallback).</summary>
/// <param name="Preset">The preset.</param>
/// <param name="Video">The chosen video encoder, or null for audio-only presets.</param>
/// <param name="Audio">The chosen audio encoder, or null for GIF.</param>
public sealed record ExportEncoding(ExportPreset Preset, EncoderTemplate? Video, EncoderTemplate? Audio);
