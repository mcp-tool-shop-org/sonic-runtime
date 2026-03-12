namespace SonicRuntime.Engine;

/// <summary>
/// Maps opaque handles to active playback instances.
/// No leases. No policy. No product semantics.
///
/// Handle format: "h_" + 12 hex chars (e.g., "h_a1b2c3d4e5f6").
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

    public void RemoveSlot(string handle)
    {
        _slots.Remove(handle);
    }
}

/// <summary>
/// Minimal state for one active playback item.
/// This will grow as real audio backends are wired in.
/// </summary>
public sealed class PlaybackSlot
{
    public string Handle { get; }
    public string? AssetRef { get; set; }
    public PlaybackStatus Status { get; set; } = PlaybackStatus.Loaded;
    public float Volume { get; set; } = 1.0f;
    public float Pan { get; set; }
    public bool Loop { get; set; }
    public long PositionMs { get; set; }
    public long? DurationMs { get; set; }

    public PlaybackSlot(string handle) => Handle = handle;
}

public enum PlaybackStatus
{
    Loaded,
    Playing,
    Paused,
    Stopped
}
