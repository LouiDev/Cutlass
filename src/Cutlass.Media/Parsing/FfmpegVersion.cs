namespace Cutlass.Media.Parsing;

/// <summary>What <c>ffmpeg -version</c> reports that Cutlass relies on.</summary>
/// <param name="BuildId">The build id, for example <c>N-126308-gd411d9e752-20260828</c> or <c>6.1.1-3ubuntu5</c>.</param>
/// <param name="AvfilterMajor">The libavfilter major version.</param>
/// <param name="AvfilterMinor">The libavfilter minor version.</param>
public sealed record FfmpegVersion(string BuildId, int AvfilterMajor, int AvfilterMinor)
{
    /// <summary>A short display form, for example <c>N-126308, libavfilter 12</c>.</summary>
    public string DisplayText
    {
        get
        {
            // Git builds print N-<rev>-g<hash>-<date>; the first two parts are what people recognise.
            var shortId = BuildId.StartsWith("N-", StringComparison.Ordinal) && BuildId.IndexOf('-', 2) is var dash and > 0
                ? BuildId[..dash]
                : BuildId;
            return $"{shortId}, libavfilter {AvfilterMajor}";
        }
    }
}
