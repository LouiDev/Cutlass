namespace Cutlass.Media.Settings;

/// <summary>When Cutlass builds 540-line proxies (DESIGN 10, TECH_BRIEF 6.6).</summary>
public enum ProxyPolicy
{
    /// <summary>Above 1080 lines, hevc/av1/vp9, VFR, or above 50 Mb/s.</summary>
    Auto = 0,

    /// <summary>For every video file.</summary>
    Always = 1,

    /// <summary>Never.</summary>
    Never = 2,
}
