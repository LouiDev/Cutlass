namespace Cutlass.Media.Preview.Audio;

/// <summary>The live mixer settings of one audio track (TECH_BRIEF 8.2).</summary>
/// <param name="TrackId">The track id, which maps the setting onto the track's channel pair.</param>
/// <param name="Muted">True when the track is silent.</param>
/// <param name="Solo">True when the track is soloed.</param>
/// <param name="GainDb">The track gain in dB.</param>
public sealed record MixerTrack(string TrackId, bool Muted, bool Solo, double GainDb);
