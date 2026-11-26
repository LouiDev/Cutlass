using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Cutlass.App.Controls;
using Cutlass.Media.Preview;

namespace Cutlass.App.Services.Playback;

/// <summary>
/// Presents preview frames (TECH_BRIEF 10.3). <see cref="Present"/> runs on the frame server's worker and only
/// keeps the newest frame with <see cref="Interlocked.Exchange{T}(ref T, T)"/>. On the next animation frame, on the
/// UI thread, the frame is copied row by row into the back <see cref="WriteableBitmap"/>, the bitmaps are swapped
/// on the <see cref="VideoSurface"/>, and the pooled buffer is returned. While playback runs
/// (<see cref="StartPlayback"/>) every animation frame asks the playback callback for the frame due instead, scrub
/// frames are dropped, and animation frames keep being requested until <see cref="StopPlayback"/>. For the two-up trim
/// feedback (DESIGN 4.3: roll and slip) a second surface can be attached; the frame of
/// <see cref="SecondaryRequestId"/> goes there instead of the main surface.
/// </summary>
public sealed class FramePresenter : IVideoFrameSink
{
    // How long replaced bitmaps live on after a size change (the compositor may still draw the last frame).
    private static readonly TimeSpan RetiredBitmapDelay = TimeSpan.FromSeconds(2);

    private VideoFrame? _pending;
    private int _scheduled;
    private VideoSurface? _surface;
    private WriteableBitmap? _front;
    private WriteableBitmap? _back;
    private Func<VideoFrame?>? _playback;
    private VideoSurface? _secondary;
    private WriteableBitmap? _secondaryFront;
    private WriteableBitmap? _secondaryBack;
    private long _secondaryRequestId = -1;
    private long _retiredSecondaryId = -1;

    /// <summary>Raised on the UI thread after a frame was shown: (request id, timeline frame).</summary>
    public event Action<long, long>? FramePresented;

    /// <summary>Raised on the UI thread after a playback frame was shown, with its timeline frame.</summary>
    public event Action<long>? PlaybackFramePresented;

    /// <summary>
    /// The request whose frame is shown on the secondary surface (the right half of a two-up), or -1. Every other
    /// request goes to the main surface.
    /// </summary>
    public long SecondaryRequestId
    {
        get => Interlocked.Read(ref _secondaryRequestId);
        set
        {
            // A right-hand frame that arrives after its two-up ended must not land on the main surface.
            var old = Interlocked.Exchange(ref _secondaryRequestId, value);
            if (old >= 0 && old != value)
            {
                Interlocked.Exchange(ref _retiredSecondaryId, old);
            }
        }
    }

    /// <summary>The request id of the frame on screen, or -1.</summary>
    public long PresentedRequestId { get; private set; } = -1;

    /// <summary>True between <see cref="StartPlayback"/> and <see cref="StopPlayback"/>.</summary>
    public bool IsPlaybackActive => _playback is not null;

    /// <summary>Playback frames shown since the last <see cref="StartPlayback"/>.</summary>
    public long PlaybackFramesShown { get; private set; }

    /// <summary>The timeline frame of the last playback frame shown, or -1.</summary>
    public long LastPlaybackFrame { get; private set; } = -1;

    /// <summary>
    /// Switches to playback (UI thread, TECH_BRIEF 10.3): on every animation frame <paramref name="nextFrame"/> is
    /// called and the frame it returns (if any) is copied and then returned to its pool.
    /// </summary>
    /// <param name="nextFrame">Returns the frame due now, or null to keep the current one.</param>
    public void StartPlayback(Func<VideoFrame?> nextFrame)
    {
        ArgumentNullException.ThrowIfNull(nextFrame);
        Dispatcher.UIThread.VerifyAccess();
        _playback = nextFrame;
        PlaybackFramesShown = 0;
        LastPlaybackFrame = -1;
        Schedule();
    }

    /// <summary>Leaves playback; scrub frames are shown again (UI thread).</summary>
    public void StopPlayback()
    {
        Dispatcher.UIThread.VerifyAccess();
        _playback = null;
    }

    /// <summary>Shows frames on a surface (UI thread). A pending frame is shown right away.</summary>
    /// <param name="surface">The viewer surface.</param>
    public void Attach(VideoSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Dispatcher.UIThread.VerifyAccess();
        _surface = surface;
        surface.Frame = _front;
        Schedule();
    }

    /// <summary>Stops showing frames on the surface (UI thread).</summary>
    /// <param name="surface">The surface that was attached.</param>
    public void Detach(VideoSurface surface)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ReferenceEquals(_surface, surface))
        {
            _surface = null;
        }
    }

    /// <summary>Shows the frames of <see cref="SecondaryRequestId"/> on a second surface (UI thread).</summary>
    /// <param name="surface">The right surface of the two-up.</param>
    public void AttachSecondary(VideoSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Dispatcher.UIThread.VerifyAccess();
        _secondary = surface;
        surface.Frame = _secondaryFront;
    }

    /// <summary>Stops showing frames on the second surface (UI thread).</summary>
    /// <param name="surface">The surface that was attached.</param>
    public void DetachSecondary(VideoSurface surface)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ReferenceEquals(_secondary, surface))
        {
            _secondary = null;
        }
    }

    /// <inheritdoc />
    public void Present(VideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Interlocked.Exchange(ref _pending, frame)?.Return();
        Schedule();
    }

    /// <summary>Drops any pending frame and blanks the surface (UI thread), for example when a project closes.</summary>
    public void Clear()
    {
        Dispatcher.UIThread.VerifyAccess();
        Interlocked.Exchange(ref _pending, null)?.Return();
        PresentedRequestId = -1;
        if (_surface is not null)
        {
            _surface.Frame = null;
        }

        if (_secondary is not null)
        {
            _secondary.Frame = null;
        }
    }

    private void Schedule()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 0)
        {
            Dispatcher.UIThread.Post(RequestTick);
        }
    }

    private void RequestTick()
    {
        // Only request animation frames while a frame is pending (TECH_BRIEF 10.3).
        if (_surface is not null && TopLevel.GetTopLevel(_surface) is { } topLevel)
        {
            topLevel.RequestAnimationFrame(OnAnimationFrame);
        }
        else
        {
            Volatile.Write(ref _scheduled, 0);
        }
    }

    private void OnAnimationFrame(TimeSpan time)
    {
        Volatile.Write(ref _scheduled, 0);
        if (_playback is { } next)
        {
            PresentPlayback(next);
            return;
        }

        var frame = Interlocked.Exchange(ref _pending, null);
        if (frame is null)
        {
            return;
        }

        try
        {
            if (_surface is null)
            {
                // Keep it for the next Attach.
                if (Interlocked.CompareExchange(ref _pending, frame, null) is null)
                {
                    return;
                }

                frame.Return();
                return;
            }

            if (frame.RequestId == Interlocked.Read(ref _retiredSecondaryId))
            {
                frame.Return();
                return;
            }

            if (frame.RequestId == SecondaryRequestId && _secondary is { } secondary)
            {
                Show(frame, secondary, ref _secondaryFront, ref _secondaryBack);
            }
            else
            {
                Show(frame, _surface, ref _front, ref _back);
            }

            frame.Return();
        }
        catch
        {
            frame.Return();
            throw;
        }

        PresentedRequestId = frame.RequestId;
        FramePresented?.Invoke(frame.RequestId, frame.TimelineFrame);
    }

    private void PresentPlayback(Func<VideoFrame?> next)
    {
        // Scrub frames that arrive while playing are stale.
        Interlocked.Exchange(ref _pending, null)?.Return();
        VideoFrame? frame = null;
        try
        {
            frame = next();
            if (frame is not null && _surface is not null)
            {
                Show(frame, _surface, ref _front, ref _back);
                PlaybackFramesShown++;
                LastPlaybackFrame = frame.TimelineFrame;
            }
        }
        finally
        {
            frame?.Return();
        }

        if (frame is not null)
        {
            PlaybackFramePresented?.Invoke(frame.TimelineFrame);
        }

        // Keep ticking while playing: the callback also drives the playhead and the meter.
        if (_playback is not null)
        {
            Schedule();
        }
    }

    private static void Show(VideoFrame frame, VideoSurface surface, ref WriteableBitmap? front, ref WriteableBitmap? back)
    {
        var size = new PixelSize(frame.Width, frame.Height);
        if (back is null || front is null || back.PixelSize != size)
        {
            // Both bitmaps are recreated when the size changes, which with Auto quality happens on every play and stop
            // (960x540 while playing). The old ones are disposed a moment later, because the compositor may still be
            // drawing the last one; leaving them to the GC let native pixel memory pile up over many play/stop cycles.
            var oldFront = front;
            var oldBack = back;
            front = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            back = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            if (oldFront is not null || oldBack is not null)
            {
                DispatcherTimer.RunOnce(
                    () =>
                    {
                        oldFront?.Dispose();
                        oldBack?.Dispose();
                    },
                    RetiredBitmapDelay);
            }
        }

        var rowBytes = frame.Width * 4;
        using (var fb = back.Lock())
        {
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Buffer, y * frame.Stride, fb.Address + (y * fb.RowBytes), rowBytes);
            }
        }

        (front, back) = (back, front);
        surface.Frame = front;
    }
}
