using Silk.NET.OpenAL;

namespace SonicRuntime.Engine;

/// <summary>
/// OpenAL Soft backend via Silk.NET.
/// Wraps all unsafe pointer operations behind safe method calls.
///
/// Multi-device model: one device/context pair per output endpoint, lazily opened.
/// Sources and buffers created on a context play to that device.
/// The default (null device name) uses the system default output.
/// </summary>
public sealed unsafe class OpenAlBackend : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private bool _disposed;

    // Default device/context (opened at construction)
    private Device* _defaultDevice;
    private Context* _defaultContext;
    private readonly string? _defaultDeviceName;

    // Per-device context cache: device name → (Device*, Context*)
    // Lazily opened on first use. The default device is NOT in this map.
    private readonly Dictionary<string, (nint Device, nint Context)> _deviceContexts = new();

    // ALC_ENUMERATE_ALL_EXT constants
    private const int ALC_ALL_DEVICES_SPECIFIER = 0x1013;
    private const int ALC_DEFAULT_ALL_DEVICES_SPECIFIER = 0x1012;

    public OpenAlBackend(string? deviceName = null)
    {
        _alc = ALContext.GetApi();
        _al = AL.GetApi();

        _defaultDevice = _alc.OpenDevice(deviceName);
        if (_defaultDevice == null)
            throw new Protocol.RuntimeException(
                "device_unavailable",
                $"Failed to open audio device: {deviceName ?? "(default)"}",
                retryable: true);

        _defaultContext = _alc.CreateContext(_defaultDevice, null);
        if (_defaultContext == null)
        {
            _alc.CloseDevice(_defaultDevice);
            throw new Protocol.RuntimeException(
                "device_unavailable",
                "Failed to create OpenAL context",
                retryable: true);
        }

        _defaultDeviceName = deviceName;
        _alc.MakeContextCurrent(_defaultContext);
    }

    // ── Device enumeration ──

    /// <summary>
    /// Enumerate all hardware audio output devices via ALC_ENUMERATE_ALL_EXT.
    /// Returns (deviceName, isDefault) pairs.
    /// </summary>
    public List<(string Name, bool IsDefault)> EnumerateDevices()
    {
        var result = new List<(string, bool)>();
        string defaultName = "";

        var alcGetStringPtr = GetAlcGetStringPtr();
        if (alcGetStringPtr != null)
        {
            var defaultPtr = alcGetStringPtr(null, ALC_DEFAULT_ALL_DEVICES_SPECIFIER);
            if (defaultPtr != null)
                defaultName = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)defaultPtr) ?? "";

            var rawPtr = alcGetStringPtr(null, ALC_ALL_DEVICES_SPECIFIER);
            if (rawPtr != null)
            {
                while (*rawPtr != 0)
                {
                    var name = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)rawPtr)!;
                    result.Add((name, name == defaultName));
                    rawPtr += name.Length + 1;
                }
            }
        }

        return result;
    }

    // ── Multi-device context management ──

    /// <summary>
    /// Get or create a device/context pair for the given device name.
    /// null or empty returns the default device context.
    /// Thread-safety: callers must not overlap device creation for the same name
    /// (the command loop is single-threaded, so this is safe).
    /// </summary>
    private Context* GetOrCreateContext(string? deviceName)
    {
        if (string.IsNullOrEmpty(deviceName))
            return _defaultContext;

        if (_deviceContexts.TryGetValue(deviceName, out var cached))
            return (Context*)cached.Context;

        // Open a new device + context for this endpoint
        var device = _alc.OpenDevice(deviceName);
        if (device == null)
            throw new Protocol.RuntimeException(
                "device_unavailable",
                $"Failed to open audio device: {deviceName}",
                retryable: true);

        var context = _alc.CreateContext(device, null);
        if (context == null)
        {
            _alc.CloseDevice(device);
            throw new Protocol.RuntimeException(
                "device_unavailable",
                $"Failed to create OpenAL context for device: {deviceName}",
                retryable: true);
        }

        _deviceContexts[deviceName] = ((nint)device, (nint)context);
        return context;
    }

    /// <summary>
    /// Make the context for a device name current. All subsequent AL calls
    /// will operate on this context until another MakeCurrent call.
    /// </summary>
    private void MakeDeviceCurrent(string? deviceName)
    {
        var ctx = GetOrCreateContext(deviceName);
        _alc.MakeContextCurrent(ctx);
    }

    // ── Buffer management ──

    /// <summary>
    /// Create an OpenAL buffer from raw PCM data on a specific device.
    /// </summary>
    public uint CreateBuffer(byte[] pcmData, int sampleRate, int channels, int bitsPerSample, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);

        var format = GetBufferFormat(channels, bitsPerSample);
        var buffer = _al.GenBuffer();
        CheckError("GenBuffer");

        fixed (byte* data = pcmData)
        {
            _al.BufferData(buffer, format, data, pcmData.Length, sampleRate);
        }
        CheckError("BufferData");

        return buffer;
    }

    public void DeleteBuffer(uint buffer, string? deviceName = null)
    {
        if (_disposed) return;
        MakeDeviceCurrent(deviceName);
        _al.DeleteBuffer(buffer);
    }

    // ── Source management ──

    public uint CreateSource(string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);

        var source = _al.GenSource();
        CheckError("GenSource");

        // Enable relative positioning for direct pan mapping (-1..1 on X axis)
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        CheckError("SetSourceRelative");

        return source;
    }

    public void DeleteSource(uint source, string? deviceName = null)
    {
        if (_disposed) return;
        MakeDeviceCurrent(deviceName);
        _al.DeleteSource(source);
    }

    public void BindBuffer(uint source, uint buffer, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
        CheckError("BindBuffer");
    }

    public void Play(uint source, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SourcePlay(source);
        CheckError("SourcePlay");
    }

    public void Pause(uint source, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SourcePause(source);
    }

    public void Stop(uint source, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SourceStop(source);
    }

    public void SetVolume(uint source, float gain, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(gain, 0.0f, 1.0f));
    }

    /// <summary>
    /// Set pan position. -1.0 = full left, 0.0 = center, 1.0 = full right.
    /// Maps directly to X position with SourceRelative=true (validated in spike).
    /// </summary>
    public void SetPan(uint source, float pan, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SetSourceProperty(source, SourceVector3.Position,
            Math.Clamp(pan, -1.0f, 1.0f), 0f, 0f);
    }

    public void SetLooping(uint source, bool loop, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SetSourceProperty(source, SourceBoolean.Looping, loop);
    }

    /// <summary>
    /// Get the current playback state of a source.
    /// </summary>
    public SourceState GetSourceState(uint source, string? deviceName = null)
    {
        if (_disposed) return SourceState.Stopped;
        MakeDeviceCurrent(deviceName);
        _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int stateVal);
        return (SourceState)stateVal;
    }

    /// <summary>
    /// Get the current playback position in seconds.
    /// </summary>
    public float GetSourcePositionSeconds(uint source, string? deviceName = null)
    {
        if (_disposed) return 0f;
        MakeDeviceCurrent(deviceName);
        _al.GetSourceProperty(source, SourceFloat.SecOffset, out float seconds);
        return seconds;
    }

    /// <summary>
    /// Seek to a position in seconds.
    /// </summary>
    public void SetSourcePositionSeconds(uint source, float seconds, string? deviceName = null)
    {
        EnsureNotDisposed();
        MakeDeviceCurrent(deviceName);
        _al.SetSourceProperty(source, SourceFloat.SecOffset, seconds);
        var err = _al.GetError();
        if (err != AudioError.NoError)
            throw new Protocol.RuntimeException(
                "seek_unsupported", "Seek failed — source may not be seekable", retryable: false);
    }

    // ── Internals ──

    private delegate* unmanaged[Cdecl]<Device*, int, byte*> GetAlcGetStringPtr()
    {
        var ptr = (nint)_alc.GetProcAddress(null, "alcGetString");
        if (ptr == nint.Zero) return null;
        return (delegate* unmanaged[Cdecl]<Device*, int, byte*>)ptr;
    }

    private static BufferFormat GetBufferFormat(int channels, int bitsPerSample) =>
        (channels, bitsPerSample) switch
        {
            (1, 8) => BufferFormat.Mono8,
            (1, 16) => BufferFormat.Mono16,
            (2, 8) => BufferFormat.Stereo8,
            (2, 16) => BufferFormat.Stereo16,
            _ => throw new Protocol.RuntimeException(
                "unsupported_format",
                $"Unsupported audio format: {channels}ch {bitsPerSample}bit",
                retryable: false)
        };

    private void CheckError(string operation)
    {
        var err = _al.GetError();
        if (err != AudioError.NoError)
            throw new Protocol.RuntimeException(
                "invalid_source",
                $"OpenAL error in {operation}: {err}",
                retryable: false);
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OpenAlBackend));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Clean up per-device contexts
        foreach (var (name, pair) in _deviceContexts)
        {
            var ctx = (Context*)pair.Context;
            var dev = (Device*)pair.Device;
            _alc.DestroyContext(ctx);
            _alc.CloseDevice(dev);
        }
        _deviceContexts.Clear();

        // Clean up default context
        if (_defaultContext != null)
        {
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_defaultContext);
            _defaultContext = null;
        }

        if (_defaultDevice != null)
        {
            _alc.CloseDevice(_defaultDevice);
            _defaultDevice = null;
        }

        _al.Dispose();
        _alc.Dispose();
    }
}
