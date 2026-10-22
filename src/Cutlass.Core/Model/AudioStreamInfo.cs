namespace Cutlass.Core.Model;

/// <summary>Probe results for the audio stream of a media file.</summary>
/// <param name="StreamIndex">Absolute stream index in the container.</param>
/// <param name="Codec">Codec name, for example <c>aac</c>.</param>
/// <param name="SampleRate">Sample rate in Hz.</param>
/// <param name="Channels">Channel count.</param>
public sealed record AudioStreamInfo(int StreamIndex, string Codec, int SampleRate, int Channels);
