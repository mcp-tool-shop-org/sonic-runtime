namespace SonicRuntime.Engine;

/// <summary>
/// Maps opaque handles to active playback instances.
/// No leases. No policy. No product semantics.
///
/// Handle format: "h_" + 12 hex chars (e.g., "h_000000000001").
/// Handles are internal to the runtime — sonic-core never exposes them to clients.
/// </summary>
public sealed class RuntimeState
{
    private readonly Dictionary<string, PlaybackSlot> _slots = new();
    private int _counter;

    public string AllocateHandle()
    {
        var id = Interlocked.Increment(ref _counter);
        var handle = $"h_{id:x12}";
        _slots[handle] = new PlaybackSlot(handle);
        return handle;
    }

    public PlaybackSlot GetSlot(string handle)
    {
        if (!_slots.TryGetValue(handle, out var slot))
            throw new Protocol.RuntimeException("playback_not_found", $"No playback for handle: {handle}", retryable: false);
        return slot;
    }

    public bool TryGetSlot(string handle, out PlaybackSlot? slot)
    {
        return _slots.TryGetValue(handle, out slot);
    }

    /// <summary>Number of active handles.</summary>
    public int ActiveHandleCount => _slots.Count;

    public void RemoveSlot(string handle)
    {
        if (_slots.TryGetValue(handle, out var slot))
        {
            slot.Dispose();
            _slots.Remove(handle);
        }
    }
}

/// <summary>
/// State for one active playback item, including OpenAL resource IDs.
/// </summary>
public sealed class PlaybackSlot : IDisposable
{
    public string Handle { get; }
    public string? AssetRef { get; set; }
    public PlaybackStatus Status { get; set; } = PlaybackStatus.Loaded;
    public float Volume { get; set; } = 1.0f;
    public float Pan { get; set; }
    public bool Loop { get; set; }

    // OpenAL resource IDs (0 = not allocated)
    public uint Source { get; set; }
    public uint Buffer { get; set; }

    // Backend reference for cleanup
    public OpenAlBackend? Backend { get; set; }

    // WAV file stream (still needed for file-based assets)
    public FileStream? AudioStream { get; set; }

    public PlaybackSlot(string handle) => Handle = handle;

    public void Dispose()
    {
        if (Backend is not null)
        {
            if (Source != 0)
            {
                try { Backend.Stop(Source); } catch { }
                try { Backend.DeleteSource(Source); } catch { }
                Source = 0;
            }
            if (Buffer != 0)
            {
                try { Backend.DeleteBuffer(Buffer); } catch { }
                Buffer = 0;
            }
        }
        AudioStream?.Dispose();
    }
}

public enum PlaybackStatus
{
    Loaded,
    Playing,
    Paused,
    Stopped
}
