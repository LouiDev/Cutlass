namespace Cutlass.Core.Serialization.Dto;

/// <summary>The sequence in the project file. Audio is always 48 kHz; <c>sampleRate</c> is written for readers.</summary>
internal sealed class SequenceDto
{
    public int Width { get; set; } = 1920;

    public int Height { get; set; } = 1080;

    public string FrameRate { get; set; } = "30";

    public int SampleRate { get; set; } = 48_000;

    public long? InFrame { get; set; }

    public long? OutFrame { get; set; }

    public bool HasHadClips { get; set; }

    public List<MarkerDto> Markers { get; set; } = [];

    public List<TrackDto> Tracks { get; set; } = [];
}
