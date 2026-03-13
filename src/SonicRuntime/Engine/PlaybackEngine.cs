using Silk.NET.OpenAL;
using SonicRuntime.Protocol;
using SonicRuntime.Synthesis;

namespace SonicRuntime.Engine;

/// <summary>
/// Playback engine backed by OpenAL Soft via Silk.NET.
/// Manages source/buffer lifecycle per handle.
///
/// Pan mapping: sonic-core uses -1.0..1.0 (standard audio convention).
/// OpenAL with SourceRelative=true maps -1..1 on X axis directly.
/// No translation needed (validated in spike, ADR-0010).
/// </summary>
public sealed class PlaybackEngine : IDisposable
{
    private readonly RuntimeState _state;
    private readonly TextWriter _log;
    private readonly bool _audioEnabled;
    private readonly IEventWriter _events;
    private readonly OpenAlBackend? _backend;

    // Completion polling — 10ms interval, runs on a background thread
    private readonly CancellationTokenSource _pollCts = new();
    private readonly Thread? _pollThread;
    private readonly HashSet<string> _polledHandles = new();
    private readonly object _pollLock = new();

    public PlaybackEngine(RuntimeState state, OpenAlBackend? backend = null,
        bool audioEnabled = true, TextWriter? log = null, IEventWriter? events = null)
    {
        _state = state;
        _backend = backend;
        _audioEnabled = audioEnabled;
        _log = log ?? Console.Error;
        _events = events ?? NullEventWriter.Instance;

        if (_audioEnabled && _backend is not null)
        {
            _pollThread = new Thread(PollCompletionLoop)
            {
                IsBackground = true,
                Name = "openal-completion-poll"
            };
            _pollThread.Start();
        }
    }

    public Task<string> LoadAssetAsync(string assetRef)
    {
        var handle = _state.AllocateHandle();
        var slot = _state.GetSlot(handle);
        slot.AssetRef = assetRef;

        if (_audioEnabled && _backend is not null)
        {
            var filePath = ResolveAssetPath(assetRef);
            if (!File.Exists(filePath))
                throw new RuntimeException("invalid_source", $"Asset file not found: {filePath}", retryable: false);

            if (!filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                throw new RuntimeException("unsupported_format",
                    $"Only WAV files are supported for playback. Got: {Path.GetExtension(filePath)}",
                    retryable: false);

            LoadWavIntoSlot(slot, filePath);
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

        if (_audioEnabled && _backend is not null && slot.Source != 0)
        {
            _backend.SetVolume(slot.Source, volume);
            _backend.SetPan(slot.Source, pan);
            _backend.SetLooping(slot.Source, loop);
            _backend.Play(slot.Source);

            // Register for completion polling (skip for looping — never completes)
            if (!loop)
            {
                lock (_pollLock)
                {
                    _polledHandles.Add(handle);
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task PauseAsync(string handle, int fadeOutMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Paused;

        if (_audioEnabled && _backend is not null && slot.Source != 0)
            _backend.Pause(slot.Source);

        return Task.CompletedTask;
    }

    public Task ResumeAsync(string handle, int fadeInMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Playing;

        if (_audioEnabled && _backend is not null && slot.Source != 0)
            _backend.Play(slot.Source);

        return Task.CompletedTask;
    }

    public Task StopAsync(string handle, int fadeOutMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Status = PlaybackStatus.Stopped;

        // Remove from polling
        lock (_pollLock)
        {
            _polledHandles.Remove(handle);
        }

        if (_audioEnabled && _backend is not null && slot.Source != 0)
            _backend.Stop(slot.Source);

        _events.Write("playback_ended", new PlaybackEndedData
        {
            Handle = handle,
            Reason = "stopped"
        });

        _state.RemoveSlot(handle);
        return Task.CompletedTask;
    }

    public Task SeekAsync(string handle, int positionMs)
    {
        var slot = _state.GetSlot(handle);

        if (_audioEnabled && _backend is not null && slot.Source != 0)
        {
            var seconds = positionMs / 1000.0f;
            _backend.SetSourcePositionSeconds(slot.Source, seconds);
        }

        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(string handle, float level, int fadeMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Volume = level;

        if (_audioEnabled && _backend is not null && slot.Source != 0)
            _backend.SetVolume(slot.Source, level);

        return Task.CompletedTask;
    }

    public Task SetPanAsync(string handle, float value, int rampMs)
    {
        var slot = _state.GetSlot(handle);
        slot.Pan = value;

        if (_audioEnabled && _backend is not null && slot.Source != 0)
            _backend.SetPan(slot.Source, value);

        return Task.CompletedTask;
    }

    public Task<long> GetPositionMsAsync(string handle)
    {
        var slot = _state.GetSlot(handle);

        if (_audioEnabled && _backend is not null && slot.Source != 0)
        {
            var seconds = _backend.GetSourcePositionSeconds(slot.Source);
            return Task.FromResult((long)(seconds * 1000.0f));
        }

        return Task.FromResult(0L);
    }

    public Task<long?> GetDurationMsAsync(string handle)
    {
        var slot = _state.GetSlot(handle);

        // Duration is known from WAV data at load time — stored as asset metadata
        // For now, return null (same behavior as SoundFlow when duration unknown)
        // TODO: store duration in slot at load time from WAV header
        return Task.FromResult<long?>(null);
    }

    // ── WAV loading ──

    internal void LoadWavIntoSlot(PlaybackSlot slot, string filePath)
    {
        if (_backend is null) return;

        var wav = WavReader.Read(filePath);
        var buffer = _backend.CreateBuffer(wav.PcmBytes, wav.SampleRate, wav.Channels, wav.BitsPerSample);
        var source = _backend.CreateSource();
        _backend.BindBuffer(source, buffer);

        slot.Source = source;
        slot.Buffer = buffer;
        slot.Backend = _backend;
        slot.AudioStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
    }

    /// <summary>
    /// Load WAV data from a byte array (for synthesis output already in memory).
    /// </summary>
    internal void LoadWavBytesIntoSlot(PlaybackSlot slot, byte[] wavBytes)
    {
        if (_backend is null) return;

        var wav = WavReader.Read(new MemoryStream(wavBytes));
        var buffer = _backend.CreateBuffer(wav.PcmBytes, wav.SampleRate, wav.Channels, wav.BitsPerSample);
        var source = _backend.CreateSource();
        _backend.BindBuffer(source, buffer);

        slot.Source = source;
        slot.Buffer = buffer;
        slot.Backend = _backend;
    }

    // ── Completion polling ──

    /// <summary>
    /// Polls OpenAL source state at 10ms intervals to detect natural completion.
    /// Replaces SoundFlow's PlaybackEnded event callback.
    /// Spike validated: 100ms tone detected in 98ms at 10ms poll interval.
    /// </summary>
    private void PollCompletionLoop()
    {
        while (!_pollCts.Token.IsCancellationRequested)
        {
            try
            {
                Thread.Sleep(10);

                string[] handlesToCheck;
                lock (_pollLock)
                {
                    if (_polledHandles.Count == 0) continue;
                    handlesToCheck = [.. _polledHandles];
                }

                foreach (var handle in handlesToCheck)
                {
                    if (!_state.TryGetSlot(handle, out var slot) || slot is null)
                    {
                        lock (_pollLock) { _polledHandles.Remove(handle); }
                        continue;
                    }

                    if (slot.Source == 0 || _backend is null) continue;

                    var state = _backend.GetSourceState(slot.Source);
                    if (state == SourceState.Stopped && slot.Status == PlaybackStatus.Playing)
                    {
                        // Natural completion detected
                        lock (_pollLock) { _polledHandles.Remove(handle); }
                        OnNaturalCompletion(handle);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.WriteLine($"[playback] poll error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Called when OpenAL reports a source has stopped while we expected it to be playing.
    /// Guards against stop-vs-completion race: if StopAsync already cleaned up, TryGetSlot fails.
    /// </summary>
    internal void OnNaturalCompletion(string handle)
    {
        if (!_state.TryGetSlot(handle, out var slot) || slot is null)
            return;

        if (slot.Status == PlaybackStatus.Stopped)
            return;

        slot.Status = PlaybackStatus.Stopped;

        _events.Write("playback_ended", new PlaybackEndedData
        {
            Handle = handle,
            Reason = "completed"
        });

        _state.RemoveSlot(handle);
    }

    // ── Internals ──

    private static string ResolveAssetPath(string assetRef)
    {
        if (assetRef.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return assetRef[8..];
        if (assetRef.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            return assetRef[7..];
        return assetRef;
    }

    public void Dispose()
    {
        _pollCts.Cancel();
        _pollThread?.Join(timeout: TimeSpan.FromMilliseconds(500));
        _pollCts.Dispose();
    }
}
