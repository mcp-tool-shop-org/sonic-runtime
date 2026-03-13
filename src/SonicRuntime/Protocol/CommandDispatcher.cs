using System.Diagnostics;
using System.Text.Json;
using SonicRuntime.Engine;
using SonicRuntime.Synthesis;

namespace SonicRuntime.Protocol;

/// <summary>
/// Routes incoming method names to the appropriate engine component.
/// No audio logic here — just parameter extraction and delegation.
/// </summary>
public sealed class CommandDispatcher
{
    private readonly PlaybackEngine _playback;
    private readonly DeviceManager _devices;
    private readonly SynthesisEngine _synthesis;
    private readonly RuntimeState _state;
    private readonly VoiceRegistry? _voiceRegistry;
    private readonly KokoroInference? _inference;
    private readonly KokoroTokenizer? _tokenizer;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    public CommandDispatcher(
        PlaybackEngine playback,
        DeviceManager devices,
        SynthesisEngine synthesis,
        RuntimeState? state = null,
        VoiceRegistry? voiceRegistry = null,
        KokoroInference? inference = null,
        KokoroTokenizer? tokenizer = null)
    {
        _playback = playback;
        _devices = devices;
        _synthesis = synthesis;
        _state = state ?? new RuntimeState();
        _voiceRegistry = voiceRegistry;
        _inference = inference;
        _tokenizer = tokenizer;
    }

    public async Task<object?> DispatchAsync(RuntimeRequest request)
    {
        return request.Method switch
        {
            "version" => GetVersion(),
            "load_asset" => await LoadAssetAsync(request.Params),
            "play" => await PlayAsync(request.Params),
            "pause" => await PauseAsync(request.Params),
            "resume" => await ResumeAsync(request.Params),
            "stop" => await StopAsync(request.Params),
            "seek" => await SeekAsync(request.Params),
            "set_volume" => await SetVolumeAsync(request.Params),
            "set_pan" => await SetPanAsync(request.Params),
            "get_position" => await GetPositionAsync(request.Params),
            "get_duration" => await GetDurationAsync(request.Params),
            "list_devices" => await ListDevicesAsync(),
            "set_device" => await SetDeviceAsync(request.Params),
            "synthesize" => await SynthesizeAsync(request.Params),
            "get_health" => GetHealth(),
            "get_capabilities" => GetCapabilities(),
            "list_voices" => ListVoices(),
            "preload_model" => PreloadModel(),
            "get_model_status" => GetModelStatus(),
            "shutdown" => Shutdown(),
            _ => throw new RuntimeException("method_not_found", $"Unknown method: {request.Method}", retryable: false)
        };
    }

    private static VersionResult GetVersion() => new();

    private async Task<HandleResult> LoadAssetAsync(JsonElement? p)
    {
        var assetRef = GetRequiredString(p, "asset_ref");
        var handle = await _playback.LoadAssetAsync(assetRef);
        return new HandleResult { Handle = handle };
    }

    private async Task<object?> PlayAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var volume = GetOptionalFloat(p, "volume") ?? 1.0f;
        var pan = GetOptionalFloat(p, "pan") ?? 0.0f;
        var fadeInMs = GetOptionalInt(p, "fade_in_ms") ?? 0;
        var loop = GetOptionalBool(p, "loop") ?? false;
        await _playback.PlayAsync(handle, volume, pan, fadeInMs, loop);
        return null;
    }

    private async Task<object?> PauseAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var fadeOutMs = GetOptionalInt(p, "fade_out_ms") ?? 0;
        await _playback.PauseAsync(handle, fadeOutMs);
        return null;
    }

    private async Task<object?> ResumeAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var fadeInMs = GetOptionalInt(p, "fade_in_ms") ?? 0;
        await _playback.ResumeAsync(handle, fadeInMs);
        return null;
    }

    private async Task<object?> StopAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var fadeOutMs = GetOptionalInt(p, "fade_out_ms") ?? 0;
        await _playback.StopAsync(handle, fadeOutMs);
        return null;
    }

    private async Task<object?> SeekAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var positionMs = GetRequiredInt(p, "position_ms");
        await _playback.SeekAsync(handle, positionMs);
        return null;
    }

    private async Task<object?> SetVolumeAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var level = GetRequiredFloat(p, "level");
        var fadeMs = GetOptionalInt(p, "fade_ms") ?? 0;
        await _playback.SetVolumeAsync(handle, level, fadeMs);
        return null;
    }

    private async Task<object?> SetPanAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var value = GetRequiredFloat(p, "value");
        var rampMs = GetOptionalInt(p, "ramp_ms") ?? 0;
        await _playback.SetPanAsync(handle, value, rampMs);
        return null;
    }

    private async Task<PositionResult> GetPositionAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var pos = await _playback.GetPositionMsAsync(handle);
        return new PositionResult { PositionMs = pos };
    }

    private async Task<DurationResult> GetDurationAsync(JsonElement? p)
    {
        var handle = GetRequiredString(p, "handle");
        var dur = await _playback.GetDurationMsAsync(handle);
        return new DurationResult { DurationMs = dur };
    }

    private async Task<DeviceInfo[]> ListDevicesAsync()
    {
        return await _devices.ListDevicesAsync();
    }

    private async Task<object?> SetDeviceAsync(JsonElement? p)
    {
        var deviceId = GetRequiredString(p, "device_id");
        await _devices.SetDeviceAsync(deviceId);
        return null;
    }

    private async Task<HandleResult> SynthesizeAsync(JsonElement? p)
    {
        var engine = GetRequiredString(p, "engine");
        var voice = GetRequiredString(p, "voice");
        var text = GetRequiredString(p, "text");
        var speed = GetOptionalFloat(p, "speed") ?? 1.0f;
        var result = await _synthesis.SynthesizeAsync(engine, voice, text, speed);
        return new HandleResult
        {
            Handle = result.Handle,
            DurationMs = result.DurationMs,
            SampleRate = result.SampleRate,
            Channels = result.Channels
        };
    }

    // ── Introspection methods (v0.3.0) ──

    private HealthResult GetHealth()
    {
        var elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        return new HealthResult
        {
            Status = "ok",
            UptimeMs = (long)elapsedMs,
            ActiveHandles = _state.ActiveHandleCount,
            ModelLoaded = _inference?.IsLoaded ?? false,
            VoicesLoaded = _voiceRegistry?.ListVoices().Length ?? 0,
            EspeakAvailable = _tokenizer?.IsEspeakAvailable ?? false
        };
    }

    private static CapabilitiesResult GetCapabilities()
    {
        return new CapabilitiesResult
        {
            Engines = ["kokoro"],
            Features = ["playback", "synthesis", "device_management", "introspection"],
            Protocol = "ndjson-stdio-v1"
        };
    }

    private VoiceInfo[] ListVoices()
    {
        var ids = _voiceRegistry?.ListVoices() ?? [];
        var voices = new VoiceInfo[ids.Length];
        for (int i = 0; i < ids.Length; i++)
        {
            voices[i] = new VoiceInfo
            {
                Id = ids[i],
                Language = ParseLanguage(ids[i]),
                Gender = ParseGender(ids[i])
            };
        }
        return voices;
    }

    private PreloadResult PreloadModel()
    {
        if (_inference is null)
            throw new RuntimeException("synthesis_not_configured",
                "Synthesis engine not available", retryable: false);

        var loadTimeMs = _inference.Preload();
        return new PreloadResult
        {
            Loaded = true,
            LoadTimeMs = loadTimeMs
        };
    }

    private ModelStatusResult GetModelStatus()
    {
        if (_inference is null)
            return new ModelStatusResult { Loaded = false };

        return new ModelStatusResult
        {
            Loaded = _inference.IsLoaded,
            Path = _inference.ModelPath,
            LoadTimeMs = _inference.LoadTimeMs,
            InferenceCount = _inference.InferenceCount
        };
    }

    /// <summary>
    /// Parse language from Kokoro voice ID convention.
    /// First char: a=American, b=British, j=Japanese, z=Chinese, etc.
    /// </summary>
    private static string ParseLanguage(string voiceId)
    {
        if (voiceId.Length < 2) return "unknown";
        return voiceId[0] switch
        {
            'a' => "en-us",
            'b' => "en-gb",
            'j' => "ja",
            'z' => "zh",
            'e' => "es",
            'f' => "fr",
            'h' => "hi",
            'i' => "it",
            'p' => "pt-br",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Parse gender from Kokoro voice ID convention.
    /// Second char: f=female, m=male.
    /// </summary>
    private static string ParseGender(string voiceId)
    {
        if (voiceId.Length < 2) return "unknown";
        return voiceId[1] switch
        {
            'f' => "female",
            'm' => "male",
            _ => "unknown"
        };
    }

    private static object? Shutdown()
    {
        // Signal the process to exit gracefully.
        // The CancellationToken in CommandLoop will handle the rest.
        Environment.Exit(0);
        return null;
    }

    // ── Parameter extraction helpers ──
    // These are plain, non-allocating where possible.

    private static string GetRequiredString(JsonElement? p, string name)
    {
        if (p is null || !p.Value.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            throw new RuntimeException("invalid_params", $"Missing required string parameter: {name}", retryable: false);
        return prop.GetString()!;
    }

    private static int GetRequiredInt(JsonElement? p, string name)
    {
        if (p is null || !p.Value.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Number)
            throw new RuntimeException("invalid_params", $"Missing required int parameter: {name}", retryable: false);
        return prop.GetInt32();
    }

    private static float GetRequiredFloat(JsonElement? p, string name)
    {
        if (p is null || !p.Value.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.Number)
            throw new RuntimeException("invalid_params", $"Missing required float parameter: {name}", retryable: false);
        return prop.GetSingle();
    }

    private static int? GetOptionalInt(JsonElement? p, string name)
    {
        if (p is null || !p.Value.TryGetProperty(name, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return null;
    }

    private static float? GetOptionalFloat(JsonElement? p, string name)
    {
        if (p is null || !p.Value.TryGetProperty(name, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.Number)
            return prop.GetSingle();
        return null;
    }

    private static bool? GetOptionalBool(JsonElement? p, string name)
    {
        if (p is null || !p.Value.TryGetProperty(name, out var prop))
            return null;
        if (prop.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return prop.GetBoolean();
        return null;
    }
}
