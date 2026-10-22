using Cutlass.Core.Time;

namespace Cutlass.Core.Model;

/// <summary>
/// The sequence canvas and rate. Audio is fixed at 48 kHz stereo (<see cref="TimeMath.SampleRate"/>).
/// </summary>
/// <param name="Width">Canvas width in pixels.</param>
/// <param name="Height">Canvas height in pixels.</param>
/// <param name="Rate">The sequence frame rate, one of <see cref="FrameRate.Supported"/>.</param>
public sealed record SequenceSettings(int Width, int Height, FrameRate Rate);
