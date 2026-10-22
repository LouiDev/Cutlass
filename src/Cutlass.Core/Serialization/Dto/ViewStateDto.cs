namespace Cutlass.Core.Serialization.Dto;

/// <summary>The <c>.cutlass.user</c> sidecar (TECH_BRIEF 3.5).</summary>
internal sealed class ViewStateDto
{
    public double PixelsPerFrame { get; set; } = 2;

    public long ScrollFrame { get; set; }

    public double ScrollY { get; set; }

    public long Playhead { get; set; }

    public Dictionary<string, double> TrackHeights { get; set; } = [];

    public string PreviewQuality { get; set; } = "auto";

    public bool UseProxies { get; set; } = true;

    public string BinView { get; set; } = "grid";
}
