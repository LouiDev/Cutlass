using System.Runtime.InteropServices;
using Silk.NET.OpenAL;

namespace Cutlass.Media.Audio;

/// <summary>
/// Streams PCM to an OpenAL device through Silk.NET (TECH_BRIEF 5.6): one device, one context, one source and a ring
/// of <see cref="DefaultBufferCount"/> buffers (<c>Stereo16</c> at 48 kHz, 1024 frames each, 128 ms queued). OpenAL
/// Soft is loaded first (<c>soft: true</c>), then the system OpenAL. <see cref="PlayedSamples"/> is the frames of the
/// unqueued buffers plus <c>AL_SAMPLE_OFFSET</c>, which counts from the first buffer still queued. Every call is
/// serialised by a lock, and the feeder makes them all while playing.
/// </summary>
/// <remarks>
/// With <c>ALC_EXT_disconnect</c>, every service pass reads <c>ALC_CONNECTED</c>. A disconnected device (a USB headset
/// unplugged) makes <see cref="IsAvailable"/> false, freezes <see cref="PlayedSamples"/> (it raced ahead before, and
/// the video fast-forwarded), refuses buffers and raises <see cref="Disconnected"/>; the playback feeder then continues
/// on a Stopwatch clock. The next <see cref="Open"/> tries <c>alcReopenDeviceSOFT</c> when
/// <c>ALC_SOFT_reopen_device</c> exists.
/// </remarks>
public sealed unsafe class OpenAlAudioOutput : IAudioOutput
{
    /// <summary>The number of streaming buffers (TECH_BRIEF 5.6 and 8.2).</summary>
    public const int DefaultBufferCount = 6;

    /// <summary>The frames per streaming buffer.</summary>
    public const int DefaultBufferFrames = 1024;

    private const int AlcDeviceSpecifier = 0x1005;
    private const int AlcAllDevicesSpecifier = 0x1013;
    private const int AlcConnected = 0x313;

    private readonly object _gate = new();
    private readonly ALContext _alc;
    private readonly AL _al;
    private readonly Device* _device;
    private readonly Context* _context;
    private readonly Stack<uint> _free = new();
    private readonly Queue<(uint Buffer, int Frames)> _queued = new();
    private readonly delegate* unmanaged<Device*, int, int, int*, void> _getIntegerv;
    private readonly delegate* unmanaged<Device*, byte*, int*, byte> _reopenDevice;
    private readonly string? _requestedName;
    private volatile bool _connected = true;
    private uint _source;
    private uint[] _buffers = [];
    private BufferFormat _format;
    private long _unqueuedFrames;
    private long _lastPlayed;
    private bool _started;
    private bool _disposed;
    private int _underruns;

    private OpenAlAudioOutput(ALContext alc, AL al, Device* device, Context* context, string deviceName, string? requestedName, bool soft)
    {
        _alc = alc;
        _al = al;
        _device = device;
        _context = context;
        DeviceName = deviceName;
        _requestedName = requestedName;
        IsSoft = soft;
        if (alc.IsExtensionPresent(device, "ALC_EXT_disconnect"))
        {
            _getIntegerv = (delegate* unmanaged<Device*, int, int, int*, void>)alc.GetProcAddress(device, "alcGetIntegerv");
        }

        if (alc.IsExtensionPresent(device, "ALC_SOFT_reopen_device"))
        {
            _reopenDevice = (delegate* unmanaged<Device*, byte*, int*, byte>)alc.GetProcAddress(device, "alcReopenDeviceSOFT");
        }
    }

    /// <summary>
    /// Raised on a thread-pool thread when the device reports itself disconnected (<c>ALC_CONNECTED</c> is 0).
    /// </summary>
    public event Action? Disconnected;

    /// <summary>False once the device was disconnected, until a later <see cref="Open"/> reopened it.</summary>
    public bool IsAvailable => _connected;

    /// <inheritdoc />
    public string DeviceName { get; }

    /// <summary>True when OpenAL Soft from the Soft.Native package was loaded, false for the system OpenAL.</summary>
    public bool IsSoft { get; }

    /// <inheritdoc />
    public int SampleRate { get; private set; }

    /// <inheritdoc />
    public int Channels { get; private set; }

    /// <inheritdoc />
    public int BufferCount => DefaultBufferCount;

    /// <inheritdoc />
    public int BufferFrames => DefaultBufferFrames;

    /// <inheritdoc />
    public int FreeBuffers
    {
        get
        {
            lock (_gate)
            {
                if (!IsReady)
                {
                    return 0;
                }

                Service();
                return _connected ? _free.Count : 0;
            }
        }
    }

    /// <inheritdoc />
    public int QueuedBuffers
    {
        get
        {
            lock (_gate)
            {
                if (!IsReady)
                {
                    return 0;
                }

                Service();
                return _queued.Count;
            }
        }
    }

    /// <inheritdoc />
    public long PlayedSamples
    {
        get
        {
            lock (_gate)
            {
                if (!IsReady || !_connected)
                {
                    return _lastPlayed;
                }

                // AL_SAMPLE_OFFSET counts from the first buffer still queued. A buffer can finish between reclaiming
                // and reading the offset, so reclaim again until the count is stable.
                long played = _lastPlayed;
                for (var attempt = 0; attempt < 4; attempt++)
                {
                    Service();
                    if (!_connected)
                    {
                        // A disconnected device reports its queue as played at once; keep the last real position.
                        return _lastPlayed;
                    }

                    _al.GetSourceProperty(_source, GetSourceInteger.SampleOffset, out int offset);
                    _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
                    if (processed == 0)
                    {
                        var front = _queued.Count > 0 ? _queued.Peek().Frames : 0;
                        played = _unqueuedFrames + Math.Clamp(offset, 0, front);
                        break;
                    }
                }

                _lastPlayed = Math.Max(_lastPlayed, played);
                return _lastPlayed;
            }
        }
    }

    /// <inheritdoc />
    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _started;
            }
        }
    }

    /// <inheritdoc />
    public int Underruns
    {
        get
        {
            lock (_gate)
            {
                return _underruns;
            }
        }
    }

    private bool IsReady => !_disposed && _buffers.Length > 0;

    /// <summary>
    /// Opens a device: OpenAL Soft first, then the system OpenAL (TECH_BRIEF 5.6). Never throws.
    /// </summary>
    /// <param name="deviceName">The device name from <see cref="ListDevices"/>, or null for the default device.</param>
    /// <param name="failure">Why no device opened, or null on success.</param>
    /// <returns>The output, or null when no device opened.</returns>
    public static OpenAlAudioOutput? TryCreate(string? deviceName, out string? failure)
    {
        failure = null;
        foreach (var soft in new[] { true, false })
        {
            var output = TryCreate(deviceName, soft, out var reason);
            if (output is not null)
            {
                return output;
            }

            failure = failure is null ? reason : failure + " " + reason;
        }

        return null;
    }

    /// <summary>
    /// Opens a device, or falls back to a <see cref="NullAudioOutput"/> (TECH_BRIEF 5.6) whose
    /// <see cref="IAudioOutput.IsAvailable"/> is false, so the status bar can say "Audio unavailable".
    /// </summary>
    /// <param name="deviceName">The device name, or null for the default device.</param>
    /// <param name="failure">Why no device opened, or null on success.</param>
    /// <returns>The output.</returns>
    public static IAudioOutput CreateOrFallback(string? deviceName, out string? failure)
    {
        var output = TryCreate(deviceName, out failure);
        if (output is null && deviceName is not null)
        {
            // A configured device that disappeared: the default device is better than silence.
            output = TryCreate(null, out var defaultFailure);
            failure = output is null ? failure + " " + defaultFailure : null;
        }

        return output is not null ? output : new NullAudioOutput();
    }

    /// <summary>
    /// Lists the output devices with <c>ALC_ALL_DEVICES_SPECIFIER</c> (falling back to <c>ALC_DEVICE_SPECIFIER</c>).
    /// Never throws; an empty list means OpenAL could not be loaded.
    /// </summary>
    /// <returns>The device names.</returns>
    public static IReadOnlyList<string> ListDevices()
    {
        foreach (var soft in new[] { true, false })
        {
            try
            {
                using var alc = ALContext.GetApi(soft);
                var getString = (delegate* unmanaged<Device*, int, byte*>)alc.GetProcAddress(null, "alcGetString");
                if (getString == null)
                {
                    continue;
                }

                var parameter = alc.IsExtensionPresent(null, "ALC_ENUMERATE_ALL_EXT") ? AlcAllDevicesSpecifier : AlcDeviceSpecifier;
                var list = ParseDeviceList(getString(null, parameter));
                if (list.Count > 0)
                {
                    return list;
                }
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
                or FileNotFoundException or InvalidOperationException or PlatformNotSupportedException)
            {
                // Try the next library.
            }
        }

        return [];
    }

    /// <inheritdoc />
    public void Open(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 2);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_connected)
            {
                TryReopenLocked();
            }

            if (_buffers.Length == 0)
            {
                _alc.MakeContextCurrent(_context);
                _source = _al.GenSource();
                _buffers = _al.GenBuffers(DefaultBufferCount);
                ThrowOnError("creating the source and buffers");
            }

            SampleRate = sampleRate;
            Channels = channels;
            _format = channels == 2 ? BufferFormat.Stereo16 : BufferFormat.Mono16;
            _underruns = 0;
            FlushLocked();
        }
    }

    /// <inheritdoc />
    public bool Enqueue(ReadOnlySpan<short> samples)
    {
        lock (_gate)
        {
            if (!IsReady)
            {
                throw new InvalidOperationException("Open the output before enqueueing samples.");
            }

            if (samples.Length == 0 || samples.Length % Channels != 0 || samples.Length / Channels > DefaultBufferFrames)
            {
                throw new ArgumentException($"A buffer holds 1 to {DefaultBufferFrames} whole frames.", nameof(samples));
            }

            Service();
            if (!_connected || _free.Count == 0)
            {
                return false;
            }

            var buffer = _free.Pop();
            fixed (short* data = samples)
            {
                _al.BufferData(buffer, _format, data, samples.Length * sizeof(short), SampleRate);
            }

            _al.SourceQueueBuffers(_source, 1, &buffer);
            _queued.Enqueue((buffer, samples.Length / Channels));
            ThrowOnError("queueing a buffer");

            // A started source that ran dry stopped by itself; new data restarts it (an underrun).
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            if (_started && (SourceState)state != SourceState.Playing)
            {
                _al.SourcePlay(_source);
                _underruns++;
            }

            return true;
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_gate)
        {
            if (!IsReady || _started || !_connected)
            {
                return;
            }

            _started = true;
            _al.SourcePlay(_source);
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        lock (_gate)
        {
            if (!IsReady || !_started)
            {
                return;
            }

            _started = false;
            _al.SourcePause(_source);
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            if (!IsReady)
            {
                return;
            }

            _started = false;
            _al.SourceStop(_source);
        }
    }

    /// <inheritdoc />
    public void Flush()
    {
        lock (_gate)
        {
            if (IsReady)
            {
                FlushLocked();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_buffers.Length > 0)
                {
                    _al.SourceStop(_source);
                    _al.SetSourceProperty(_source, SourceInteger.Buffer, 0);
                    _al.DeleteSource(_source);
                    _al.DeleteBuffers(_buffers);
                }

                _alc.MakeContextCurrent(null);
                _alc.DestroyContext(_context);
                _alc.CloseDevice(_device);
            }
            finally
            {
                _al.Dispose();
                _alc.Dispose();
            }
        }
    }

    private static OpenAlAudioOutput? TryCreate(string? deviceName, bool soft, out string? failure)
    {
        ALContext? alc = null;
        AL? al = null;
        try
        {
            alc = ALContext.GetApi(soft);
            al = AL.GetApi(soft);

            // An empty name opens the default device.
            var device = alc.OpenDevice(deviceName ?? string.Empty);
            if (device == null)
            {
                failure = $"OpenAL ({(soft ? "soft" : "system")}) could not open {(deviceName is null ? "the default device" : $"\"{deviceName}\"")}.";
                al.Dispose();
                alc.Dispose();
                return null;
            }

            var context = alc.CreateContext(device, null);
            if (context == null || !alc.MakeContextCurrent(context))
            {
                if (context != null)
                {
                    alc.DestroyContext(context);
                }

                alc.CloseDevice(device);
                failure = $"OpenAL ({(soft ? "soft" : "system")}) could not create a context.";
                al.Dispose();
                alc.Dispose();
                return null;
            }

            var name = alc.GetContextProperty(device, GetContextString.DeviceSpecifier);
            failure = null;
            return new OpenAlAudioOutput(alc, al, device, context, string.IsNullOrEmpty(name) ? deviceName ?? "Default device" : name, deviceName, soft);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException
            or FileNotFoundException or InvalidOperationException or PlatformNotSupportedException)
        {
            al?.Dispose();
            alc?.Dispose();
            failure = $"OpenAL ({(soft ? "soft" : "system")}) could not be loaded: {ex.Message}";
            return null;
        }
    }

    private static List<string> ParseDeviceList(byte* list)
    {
        var names = new List<string>();
        if (list == null)
        {
            return names;
        }

        // A list of NUL-terminated UTF-8 strings ending with an empty string.
        var p = list;
        while (*p != 0)
        {
            var name = Marshal.PtrToStringUTF8((nint)p) ?? string.Empty;
            names.Add(name);
            p += System.Text.Encoding.UTF8.GetByteCount(name) + 1;
        }

        return names;
    }

    // Reclaims finished buffers and restarts a started source that stopped with buffers still queued (an underrun).
    // A disconnected device is only noted: nothing it reports afterwards is real.
    private void Service()
    {
        if (!CheckConnectedLocked())
        {
            return;
        }

        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        for (var i = 0; i < processed && _queued.Count > 0; i++)
        {
            uint buffer;
            _al.SourceUnqueueBuffers(_source, 1, &buffer);
            var (_, frames) = _queued.Dequeue();
            _unqueuedFrames += frames;
            _free.Push(buffer);
        }

        if (_started && _queued.Count > 0)
        {
            _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
            if ((SourceState)state == SourceState.Stopped)
            {
                _al.SourcePlay(_source);
                _underruns++;
            }
        }
    }

    // ALC_CONNECTED through ALC_EXT_disconnect; without the extension the device always counts as connected.
    private bool CheckConnectedLocked()
    {
        if (!_connected)
        {
            return false;
        }

        if (_getIntegerv == null)
        {
            return true;
        }

        var connected = 1;
        _getIntegerv(_device, AlcConnected, 1, &connected);
        if (connected != 0)
        {
            return true;
        }

        _connected = false;
        _started = false;
        var handler = Disconnected;
        if (handler is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => handler());
        }

        return false;
    }

    // alcReopenDeviceSOFT: the configured device first, then the default one. The context, source and buffers survive.
    private void TryReopenLocked()
    {
        if (_reopenDevice == null)
        {
            return;
        }

        var reopened = false;
        if (_requestedName is not null)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(_requestedName + "\0");
            fixed (byte* name = bytes)
            {
                reopened = _reopenDevice(_device, name, null) != 0;
            }
        }

        if (!reopened)
        {
            reopened = _reopenDevice(_device, null, null) != 0;
        }

        if (reopened)
        {
            _connected = true;
        }
    }

    private void FlushLocked()
    {
        _started = false;
        _al.SourceStop(_source);
        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        for (var i = 0; i < processed; i++)
        {
            uint buffer;
            _al.SourceUnqueueBuffers(_source, 1, &buffer);
        }

        // Detaching the queue returns every buffer, including any the stop did not mark processed.
        _al.SetSourceProperty(_source, SourceInteger.Buffer, 0);

        // A stopped source reports every newly queued buffer as processed, which made the next run's clock race to
        // its end. Rewinding returns the source to AL_INITIAL, like a fresh one.
        _al.SourceRewind(_source);
        _al.GetError();
        _queued.Clear();
        _free.Clear();
        foreach (var buffer in _buffers)
        {
            _free.Push(buffer);
        }

        _unqueuedFrames = 0;
        _lastPlayed = 0;
    }

    private void ThrowOnError(string what)
    {
        var error = _al.GetError();
        if (error != AudioError.NoError)
        {
            throw new InvalidOperationException($"OpenAL failed while {what}: {error}.");
        }
    }
}
