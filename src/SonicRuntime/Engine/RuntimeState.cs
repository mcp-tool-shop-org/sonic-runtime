using SoundFlow.Components;
using SoundFlow.Providers;

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
/// State for one active playback item, including SoundFlow objects.
/// </summary>
public sealed class PlaybackSlot : IDisposable
{
    public string Handle { get; }
    public string? AssetRef { get; set; }
    public PlaybackStatus Status { get; set; } = PlaybackStatus.Loaded;
    public float Volume { get; set; } = 1.0f;
    public float Pan { get; set; }
    public bool Loop { get; set; }

    // SoundFlow objects — nullable because they're set after load
    public FileStream? AudioStream { get; set; }
    public StreamDataProvider? DataProvider { get; set; }
    public SoundPlayer? Player { get; set; }

    public PlaybackSlot(string handle) => Handle = handle;

    public void Dispose()
    {
        if (Player is not null)
        {
            try { Mixer.Master.RemoveComponent(Player); } catch { }
        }
        DataProvider?.Dispose();
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
