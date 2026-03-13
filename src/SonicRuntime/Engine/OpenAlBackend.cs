using Silk.NET.OpenAL;

namespace SonicRuntime.Engine;

/// <summary>
/// OpenAL Soft backend via Silk.NET.
/// Wraps all unsafe pointer operations behind safe method calls.
///
/// Stage 1: single device, single context. All sources play on the default device.
/// Stage 2 (future): per-playback device routing via multiple contexts.
/// </summary>
public sealed unsafe class OpenAlBackend : IDisposable
{
    private readonly AL _al;
    private readonly ALContext _alc;
    private Device* _device;
    private Context* _context;
    private bool _disposed;

    // ALC_ENUMERATE_ALL_EXT constant — Silk.NET's Enumeration extension only wraps
    // ALC_ENUMERATION_EXT which returns "OpenAL Soft" as a single logical device.
    // We need the real hardware endpoint names.
    private const int ALC_ALL_DEVICES_SPECIFIER = 0x1013;
    private const int ALC_DEFAULT_ALL_DEVICES_SPECIFIER = 0x1012;

    public OpenAlBackend(string? deviceName = null)
    {
        _alc = ALContext.GetApi();
        _al = AL.GetApi();

        _device = _alc.OpenDevice(deviceName);
        if (_device == null)
            throw new Protocol.RuntimeException(
                "device_unavailable",
                $"Failed to open audio device: {deviceName ?? "(default)"}",
                retryable: true);

        _context = _alc.CreateContext(_device, null);
        if (_context == null)
        {
            _alc.CloseDevice(_device);
            throw new Protocol.RuntimeException(
                "device_unavailable",
                "Failed to create OpenAL context",
                retryable: true);
        }

        _alc.MakeContextCurrent(_context);
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

        // Get default device name
        var alcGetStringPtr = GetAlcGetStringPtr();
        if (alcGetStringPtr != null)
        {
            var defaultPtr = alcGetStringPtr(null, ALC_DEFAULT_ALL_DEVICES_SPECIFIER);
            if (defaultPtr != null)
                defaultName = System.Runtime.InteropServices.Marshal.PtrToStringAnsi((nint)defaultPtr) ?? "";

            // Get all device names (double-null terminated list)
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

    // ── Buffer management ──

    /// <summary>
    /// Create an OpenAL buffer from raw PCM data.
    /// </summary>
    public uint CreateBuffer(byte[] pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);

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

    public void DeleteBuffer(uint buffer)
    {
        if (_disposed) return;
        _alc.MakeContextCurrent(_context);
        _al.DeleteBuffer(buffer);
    }

    // ── Source management ──

    public uint CreateSource()
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);

        var source = _al.GenSource();
        CheckError("GenSource");

        // Enable relative positioning for direct pan mapping (-1..1 on X axis)
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        CheckError("SetSourceRelative");

        return source;
    }

    public void DeleteSource(uint source)
    {
        if (_disposed) return;
        _alc.MakeContextCurrent(_context);
        _al.DeleteSource(source);
    }

    public void BindBuffer(uint source, uint buffer)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)buffer);
        CheckError("BindBuffer");
    }

    public void Play(uint source)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
        _al.SourcePlay(source);
        CheckError("SourcePlay");
    }

    public void Pause(uint source)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
        _al.SourcePause(source);
    }

    public void Stop(uint source)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
        _al.SourceStop(source);
    }

    public void SetVolume(uint source, float gain)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
        _al.SetSourceProperty(source, SourceFloat.Gain, Math.Clamp(gain, 0.0f, 1.0f));
    }

    /// <summary>
    /// Set pan position. -1.0 = full left, 0.0 = center, 1.0 = full right.
    /// Maps directly to X position with SourceRelative=true (validated in spike).
    /// </summary>
    public void SetPan(uint source, float pan)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
        _al.SetSourceProperty(source, SourceVector3.Position,
            Math.Clamp(pan, -1.0f, 1.0f), 0f, 0f);
    }

    public void SetLooping(uint source, bool loop)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
        _al.SetSourceProperty(source, SourceBoolean.Looping, loop);
    }

    /// <summary>
    /// Get the current playback state of a source.
    /// </summary>
    public SourceState GetSourceState(uint source)
    {
        if (_disposed) return SourceState.Stopped;
        _alc.MakeContextCurrent(_context);
        _al.GetSourceProperty(source, GetSourceInteger.SourceState, out int stateVal);
        return (SourceState)stateVal;
    }

    /// <summary>
    /// Get the current playback position in seconds.
    /// </summary>
    public float GetSourcePositionSeconds(uint source)
    {
        if (_disposed) return 0f;
        _alc.MakeContextCurrent(_context);
        _al.GetSourceProperty(source, SourceFloat.SecOffset, out float seconds);
        return seconds;
    }

    /// <summary>
    /// Seek to a position in seconds.
    /// </summary>
    public void SetSourcePositionSeconds(uint source, float seconds)
    {
        EnsureNotDisposed();
        _alc.MakeContextCurrent(_context);
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

        if (_context != null)
        {
            _alc.MakeContextCurrent(null);
            _alc.DestroyContext(_context);
            _context = null;
        }

        if (_device != null)
        {
            _alc.CloseDevice(_device);
            _device = null;
        }

        _al.Dispose();
        _alc.Dispose();
    }
}
