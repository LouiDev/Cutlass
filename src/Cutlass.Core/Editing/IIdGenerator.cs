namespace Cutlass.Core.Editing;

/// <summary>
/// Creates model ids: a prefix plus 8 base32 characters (TECH_BRIEF 3.2). Edits that create ids take a generator as
/// their first argument; tests pass a sequential one so results are deterministic.
/// </summary>
public interface IIdGenerator
{
    /// <summary>Prefix of media asset ids.</summary>
    public const string MediaPrefix = "m_";

    /// <summary>Prefix of track ids.</summary>
    public const string TrackPrefix = "tr_";

    /// <summary>Prefix of clip ids.</summary>
    public const string ClipPrefix = "c_";

    /// <summary>Prefix of transition ids.</summary>
    public const string TransitionPrefix = "x_";

    /// <summary>Prefix of marker ids.</summary>
    public const string MarkerPrefix = "k_";

    /// <summary>Prefix of link group ids.</summary>
    public const string LinkPrefix = "l_";

    /// <summary>Returns a new id that starts with <paramref name="prefix"/>.</summary>
    /// <param name="prefix">One of the prefix constants, for example <see cref="ClipPrefix"/>.</param>
    /// <returns>The new id, for example <c>c_7f2c4k9q</c>.</returns>
    string NewId(string prefix);
}
