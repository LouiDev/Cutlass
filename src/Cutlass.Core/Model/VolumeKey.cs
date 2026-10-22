namespace Cutlass.Core.Model;

/// <summary>A volume keyframe on an audio clip.</summary>
/// <param name="Frame">Position in frames relative to the clip's <see cref="Clip.Start"/>.</param>
/// <param name="Db">Level in dB.</param>
public sealed record VolumeKey(long Frame, double Db);
