using System.Collections.Immutable;
using Cutlass.Core.Model;

namespace Cutlass.Core.Editing;

/// <summary>
/// The audio props of the clips that are heard: audio media clips and tones (effects batch 5). The timeline's volume
/// line and keyframes, the Inspector and the agent's volume keys all reach a clip's gain and keyframes through here, so
/// a tone behaves like audio media.
/// </summary>
public static class ClipAudio
{
    /// <summary>The audio props of an audio media clip or a tone.</summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The props, or null for clips that are not heard (video, titles, mattes and bars).</returns>
    public static AudioProps? Of(Clip clip) => clip switch
    {
        MediaClip { Stream: StreamKind.Audio } m => m.Audio,
        GeneratorClip g when g.Kind.IsAudio() => g.Audio,
        _ => null,
    };

    /// <summary>The clip with new audio props; clips without audio props (see <see cref="Of"/>) are returned unchanged.</summary>
    /// <param name="clip">The clip.</param>
    /// <param name="audio">The new props.</param>
    /// <returns>The clip.</returns>
    public static Clip With(Clip clip, AudioProps audio) => clip switch
    {
        MediaClip { Stream: StreamKind.Audio } m => m with { Audio = audio },
        GeneratorClip g when g.Kind.IsAudio() => g with { Audio = audio },
        _ => clip,
    };

    /// <summary>The volume keyframes of a heard clip (empty, never default, for one without keys or without audio props).</summary>
    /// <param name="clip">The clip.</param>
    /// <returns>The keyframes, sorted by frame.</returns>
    public static ImmutableArray<VolumeKey> Keys(Clip clip) =>
        Of(clip) is { Keys: { IsDefault: false } keys } ? keys : ImmutableArray<VolumeKey>.Empty;
}
