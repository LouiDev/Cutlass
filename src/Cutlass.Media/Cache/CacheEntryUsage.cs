namespace Cutlass.Media.Cache;

/// <summary>The disk use of one media cache folder, as the LRU sees it.</summary>
/// <param name="Key">The folder name (the hex part of the fingerprint).</param>
/// <param name="Bytes">The total size of its files.</param>
/// <param name="LastAccessUtc">The manifest's last access time, or the folder's write time without a manifest.</param>
public sealed record CacheEntryUsage(string Key, long Bytes, DateTime LastAccessUtc);
