namespace SonicRuntime.Engine;

/// <summary>
/// Stub playback engine. Manages handles and state transitions.
/// Real audio I/O (NAudio, CSCore, etc.) will replace the stub implementations
/// once NativeAOT compatibility is validated per ADR-0006.
/// </summary>
public sealed class PlaybackEngine
{
    private readonly RuntimeState _state;

    public PlaybackEngine(RuntimeState state)
    {
        _state = state;
    }

    public Task<string> LoadAssetAsync(string assetRef)
    {
        var handle = _state.AllocateHandle();
        var slot = _state.GetSlot(handle);
        slot.AssetRef = assetRef;
        // TODO: actually load the asset file and determine duration
        return Task.FromResult(handle);
    }

    public Task PlayAsync(string handle, float volume, float pan, int fadeInMs, bool loop)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Playing;
        slot.Volume = volume;
        slot.Pan = pan;
        slot.Loop = loop;
        // TODO: start actual audio playback
        return Task.CompletedTask;
    }

    public Task PauseAsync(string handle, int fadeOutMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Paused;
        // TODO: fade out then pause audio
        return Task.CompletedTask;
    }

    public Task ResumeAsync(string handle, int fadeInMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Playing;
        // TODO: resume audio then fade in
        return Task.CompletedTask;
    }

    public Task StopAsync(string handle, int fadeOutMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Stopped;
        // TODO: fade out then stop and release resources
        return Task.CompletedTask;
    }

    public Task SeekAsync(string handle, int positionMs)
    {
        var slot = _state.GetSlot(handle);
        slot.PositionMs = positionMs;
        // TODO: seek in actual audio stream
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(string handle, float level, int fadeMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Volume = level;
        // TODO: apply volume ramp
        return Task.CompletedTask;
    }

    public Task SetPanAsync(string handle, float value, int rampMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Pan = value;
        // TODO: apply pan ramp (sample-accurate, no zipper noise)
        return Task.CompletedTask;
    }

    public Task<long> GetPositionMsAsync(string handle)
    {
        var slot = _state.GetSlot(handle);
        return Task.FromResult(slot.PositionMs);
    }

    public Task<long?> GetDurationMsAsync(string handle)
    {
        var slot = _state.GetSlot(handle);
        return Task.FromResult(slot.DurationMs);
    }
}
