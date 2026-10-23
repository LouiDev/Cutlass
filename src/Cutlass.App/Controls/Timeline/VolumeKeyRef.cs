namespace Cutlass.App.Controls.Timeline;

/// <summary>A selected volume keyframe (DESIGN 5.5): the audio clip and the key's frame relative to the clip start.</summary>
/// <param name="ClipId">The audio clip.</param>
/// <param name="Frame">The keyframe frame, relative to the clip start.</param>
public sealed record VolumeKeyRef(string ClipId, long Frame);
