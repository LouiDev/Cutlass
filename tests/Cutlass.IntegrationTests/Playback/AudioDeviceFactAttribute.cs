using Cutlass.Media.Audio;

namespace Cutlass.IntegrationTests.Playback;

/// <summary>
/// A <see cref="FactAttribute"/> that skips itself when no OpenAL output device opens (CI machines without sound).
/// xunit 2 has no runtime skip, so the device is probed once in the constructor.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AudioDeviceFactAttribute : FactAttribute
{
    private static readonly Lazy<string?> Unavailable = new(() =>
    {
        using var output = OpenAlAudioOutput.TryCreate(null, out var failure);
        return output is null ? failure ?? "No OpenAL device opened." : null;
    });

    /// <summary>Creates the attribute and sets <see cref="FactAttribute.Skip"/> when no device opens.</summary>
    public AudioDeviceFactAttribute()
    {
        if (Unavailable.Value is { } reason)
        {
            Skip = "No audio output device: " + reason;
        }
    }
}
