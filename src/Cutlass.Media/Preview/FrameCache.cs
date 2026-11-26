using System.Security.Cryptography;
using System.Text;
using Cutlass.Compiler;
using Cutlass.Compiler.Export;
using Cutlass.Media.Preview.Playback;

namespace Cutlass.Media.Preview;

/// <summary>
/// The preview frame cache (TECH_BRIEF 8.1): an LRU of BGRA frames bounded by bytes (512 MB by default), keyed by
/// <see cref="Key"/>, the SHA-256 of the graph text, the input arguments, each input's fingerprint plus whether it is
/// the proxy, and the preview size. Unrelated edits and undo keep their hits. One instance is shared by the
/// <see cref="FrameServer"/> and <see cref="PlaybackSession"/>. Cached frames own their arrays and are never mutated.
/// Thread-safe.
/// </summary>
public sealed class FrameCache
{
    /// <summary>The default byte limit, 512 MB.</summary>
    public const long DefaultMaxBytes = 512L * 1024 * 1024;

    private readonly LinkedList<(string Key, byte[] Pixels)> _order = new();
    private readonly Dictionary<string, LinkedListNode<(string Key, byte[] Pixels)>> _map = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private long _bytes;
    private long _hits;
    private long _misses;

    /// <summary>Creates the cache.</summary>
    /// <param name="maxBytes">The maximum total size of the cached arrays.</param>
    public FrameCache(long maxBytes = DefaultMaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        MaxBytes = maxBytes;
    }

    /// <summary>The maximum total size in bytes.</summary>
    public long MaxBytes { get; }

    /// <summary>The number of cached frames.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    /// <summary>The total size of the cached arrays.</summary>
    public long Bytes
    {
        get
        {
            lock (_gate)
            {
                return _bytes;
            }
        }
    }

    /// <summary>How many lookups hit since the cache was created.</summary>
    public long Hits
    {
        get
        {
            lock (_gate)
            {
                return _hits;
            }
        }
    }

    /// <summary>How many lookups missed since the cache was created.</summary>
    public long Misses
    {
        get
        {
            lock (_gate)
            {
                return _misses;
            }
        }
    }

    /// <summary>
    /// The cache key of a compiled frame: the SHA-256 of the graph text, each input's arguments with the fingerprint
    /// of its file (proxies carry a <c>|proxy</c> suffix), and the size (TECH_BRIEF 8.1).
    /// </summary>
    /// <param name="render">The compiled single-frame render.</param>
    /// <param name="size">The output size.</param>
    /// <param name="fingerprints">Media path to fingerprint (paths without one count as unknown).</param>
    /// <returns>The lowercase hex key.</returns>
    public static string Key(CompiledRender render, PreviewSize size, IReadOnlyDictionary<string, string> fingerprints)
    {
        ArgumentNullException.ThrowIfNull(render);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(fingerprints);
        var sb = new StringBuilder(render.GraphText.Length + 256);
        sb.Append(render.GraphText).Append('\n');
        foreach (var input in render.Inputs)
        {
            foreach (var arg in FfmpegArgsBuilder.Input(input))
            {
                sb.Append(arg).Append('\u001f');
            }

            sb.Append(fingerprints.TryGetValue(input.Path, out var fp) ? fp : "?").Append('\n');
        }

        sb.Append(size.Width).Append('x').Append(size.Height);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>True when a frame is cached under the key (does not change the LRU order or the counters).</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when cached.</returns>
    public bool Contains(string key)
    {
        lock (_gate)
        {
            return _map.ContainsKey(key);
        }
    }

    /// <summary>Looks a frame up and marks it most recently used.</summary>
    /// <param name="key">The key.</param>
    /// <param name="pixels">The cached pixels (never write into them).</param>
    /// <returns>True on a hit.</returns>
    public bool TryGet(string key, out byte[] pixels)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                pixels = node.Value.Pixels;
                _hits++;
                return true;
            }

            _misses++;
        }

        pixels = [];
        return false;
    }

    /// <summary>
    /// Adds or replaces a frame and evicts the least recently used ones beyond <see cref="MaxBytes"/>. A frame larger
    /// than the limit is not cached.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="pixels">The pixels; the cache takes ownership of the array.</param>
    public void Add(string key, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.LongLength > MaxBytes)
        {
            return;
        }

        lock (_gate)
        {
            if (_map.Remove(key, out var existing))
            {
                _order.Remove(existing);
                _bytes -= existing.Value.Pixels.LongLength;
            }

            _map[key] = _order.AddFirst((key, pixels));
            _bytes += pixels.LongLength;
            while (_bytes > MaxBytes)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                _bytes -= last.Value.Pixels.LongLength;
            }
        }
    }

    /// <summary>Removes every frame.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _order.Clear();
            _map.Clear();
            _bytes = 0;
        }
    }
}
