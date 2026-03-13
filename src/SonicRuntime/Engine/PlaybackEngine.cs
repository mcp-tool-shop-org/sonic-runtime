using SonicRuntime.Protocol;
using SoundFlow.Abstracts;
using SoundFlow.Components;
using SoundFlow.Providers;

namespace SonicRuntime.Engine;

/// <summary>
/// Real playback engine backed by SoundFlow/MiniAudio.
/// Manages SoundPlayer lifecycle per handle.
///
/// Pan mapping: sonic-core uses -1.0..1.0 (standard audio convention).
/// SoundFlow v1.1.1 uses 0.0..1.0 (0=left, 0.5=center, 1=right).
/// All pan values from sonic-core are mapped before reaching SoundFlow.
/// </summary>
public sealed class PlaybackEngine
{
    private readonly RuntimeState _state;
    private readonly TextWriter _log;
    private readonly bool _audioEnabled;
    private readonly IEventWriter _events;

    /// <param name="state">Runtime state store</param>
    /// <param name="audioEnabled">When false, skip file I/O and SoundFlow calls (for testing)</param>
    /// <param name="log">Diagnostic output (stderr)</param>
    /// <param name="events">Event writer for runtime events</param>
    public PlaybackEngine(RuntimeState state, bool audioEnabled = true, TextWriter? log = null, IEventWriter? events = null)
    {
        _state = state;
        _audioEnabled = audioEnabled;
        _log = log ?? Console.Error;
        _events = events ?? NullEventWriter.Instance;
    }

    public Task<string> LoadAssetAsync(string assetRef)
    {
        var handle = _state.AllocateHandle();
        var slot = _state.GetSlot(handle);
        slot.AssetRef = assetRef;

        if (_audioEnabled)
        {
            var filePath = ResolveAssetPath(assetRef);
            if (!File.Exists(filePath))
                throw new Protocol.RuntimeException("invalid_source", $"Asset file not found: {filePath}", retryable: false);

            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var provider = new StreamDataProvider(stream);
            var player = new SoundPlayer(provider);

            slot.AudioStream = stream;
            slot.DataProvider = provider;
            slot.Player = player;
        }

        return Task.FromResult(handle);
    }

    public Task PlayAsync(string handle, float volume, float pan, int fadeInMs, bool loop)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Playing;
        slot.Volume = volume;
        slot.Pan = pan;
        slot.Loop = loop;

        if (_audioEnabled && slot.Player is not null)
        {
            slot.Player.Volume = Math.Clamp(volume, 0.0f, 1.0f);
            slot.Player.Pan = MapPan(pan);
            slot.Player.IsLooping = loop;
            Mixer.Master.AddComponent(slot.Player);
            slot.Player.Play();
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync(string handle, int fadeOutMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Paused;

        if (_audioEnabled && slot.Player is not null)
            slot.Player.Pause();

        return Task.CompletedTask;
    }

    public Task ResumeAsync(string handle, int fadeInMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Playing;

        if (_audioEnabled && slot.Player is not null)
            slot.Player.Play();

        return Task.CompletedTask;
    }

    public Task StopAsync(string handle, int fadeOutMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Stopped;

        if (_audioEnabled && slot.Player is not null)
        {
            slot.Player.Stop();
            Mixer.Master.RemoveComponent(slot.Player);
        }

        _events.Write("playback_ended", new PlaybackEndedData
        {
            Handle = handle,
            Reason = "stopped"
        });

        return Task.CompletedTask;
    }

    public Task SeekAsync(string handle, int positionMs)
    {
        var slot = _state.GetSlot(handle);

        if (_audioEnabled && slot.Player is not null)
        {
            var seconds = positionMs / 1000.0f;
            if (!slot.Player.Seek(seconds))
            {
                throw new Protocol.RuntimeException(
                    "seek_unsupported", "Seek failed — source may not be seekable", retryable: false);
            }
        }

        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(string handle, float level, int fadeMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Volume = level;

        if (_audioEnabled && slot.Player is not null)
            slot.Player.Volume = Math.Clamp(level, 0.0f, 1.0f);

        return Task.CompletedTask;
    }

    public Task SetPanAsync(string handle, float value, int rampMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Pan = value;

        if (_audioEnabled && slot.Player is not null)
            slot.Player.Pan = MapPan(value);

        return Task.CompletedTask;
    }

    public Task<long> GetPositionMsAsync(string handle)
    {
        var slot = _state.GetSlot(handle);

        if (_audioEnabled && slot.Player is not null)
        {
            var posMs = (long)(slot.Player.Time * 1000.0f);
            return Task.FromResult(posMs);
        }

        return Task.FromResult(0L);
    }

    public Task<long?> GetDurationMsAsync(string handle)
    {
        var slot = _state.GetSlot(handle);

        if (_audioEnabled && slot.Player is not null)
        {
            var dur = slot.Player.Duration;
            if (dur <= 0) return Task.FromResult<long?>(null);
            return Task.FromResult<long?>((long)(dur * 1000.0f));
        }

        return Task.FromResult<long?>(null);
    }

    // ── Internals ──

    /// <summary>
    /// Map sonic-core pan (-1.0..1.0) to SoundFlow pan (0.0..1.0).
    /// Defensive clamp because callers are sometimes little chaos goblins.
    /// </summary>
    private static float MapPan(float corePan)
    {
        return Math.Clamp((corePan + 1.0f) / 2.0f, 0.0f, 1.0f);
    }

    private static string ResolveAssetPath(string assetRef)
    {
        if (assetRef.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return assetRef[8..]; // strip file:///
        if (assetRef.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return assetRef[7..];
        return assetRef; // treat as direct path
    }

    private static void EnsurePlayerLoaded(PlaybackSlot slot)
    {
        if (slot.Player is null)
            throw new Protocol.RuntimeException(
                "playback_not_found", $"Handle {slot.Handle} has no loaded player", retryable: false);
    }
}
