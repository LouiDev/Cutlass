using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Cutlass.App.Controls.Timeline;

/// <summary>
/// Thumbnail sheets (1600x900 JPEG sprites, TECH_BRIEF 6.5) decoded as <see cref="Bitmap"/>s, shared by the timeline
/// filmstrips, the media bin hover skim and the viewer placeholder (TECH_BRIEF 10.2: an LRU of 64). Every member runs
/// on the UI thread. A miss starts decoding on the thread pool (never file I/O on the UI thread) and returns null;
/// <see cref="Loaded"/> asks for a repaint once the sheet is ready. An evicted sheet is disposed two seconds later;
/// <see cref="Evicted"/> tells holders of images cut from it (a <c>CroppedBitmap</c>) to let go before that.
/// </summary>
public sealed class FilmstripCache
{
    /// <summary>How many sheets stay decoded.</summary>
    public const int DefaultCapacity = 64;

    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DisposeDelay = TimeSpan.FromSeconds(2);

    private readonly int _capacity;
    private readonly Dictionary<string, LinkedListNode<(string Path, Bitmap Bitmap)>> _map = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Path, Bitmap Bitmap)> _lru = new();
    private readonly HashSet<string> _loading = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTime> _failed = new(StringComparer.Ordinal);

    /// <summary>Creates a cache.</summary>
    /// <param name="capacity">How many sheets stay decoded.</param>
    public FilmstripCache(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Raised on the UI thread when a sheet finished decoding.</summary>
    public event EventHandler? Loaded;

    /// <summary>
    /// Raised on the UI thread with a sheet that left the cache. It is disposed two seconds later, so images that draw
    /// from it must be dropped (or cut again from a fresh <see cref="Get"/>) now.
    /// </summary>
    public event EventHandler<Bitmap>? Evicted;

    /// <summary>The cache every view shares.</summary>
    public static FilmstripCache Shared { get; } = new();

    /// <summary>The number of decoded sheets.</summary>
    public int Count => _map.Count;

    /// <summary>Returns a decoded sheet, or null while it decodes (or after a failure, retried after 5 s).</summary>
    /// <param name="path">The sheet file.</param>
    /// <returns>The bitmap, or null.</returns>
    public Bitmap? Get(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (_map.TryGetValue(path, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Bitmap;
        }

        if (_loading.Contains(path) || (_failed.TryGetValue(path, out var failedAt) && DateTime.UtcNow - failedAt < RetryAfter))
        {
            return null;
        }

        _loading.Add(path);
        _ = LoadAsync(path);
        return null;
    }

    private async Task LoadAsync(string path)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() => new Bitmap(path));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A sheet that is missing or still being renamed into place is retried later.
            _failed[path] = DateTime.UtcNow;
        }
        finally
        {
            _loading.Remove(path);
        }

        if (bitmap is null)
        {
            return;
        }

        _failed.Remove(path);
        _lru.AddFirst((path, bitmap));
        _map[path] = _lru.First!;
        while (_map.Count > _capacity && _lru.Last is { } last)
        {
            _lru.RemoveLast();
            _map.Remove(last.Value.Path);
            var old = last.Value.Bitmap;
            Evicted?.Invoke(this, old);

            // The compositor may still draw the evicted sheet in the frame being rendered.
            DispatcherTimer.RunOnce(old.Dispose, DisposeDelay);
        }

        Loaded?.Invoke(this, EventArgs.Empty);
    }
}
