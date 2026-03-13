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
    private readonly string _baseDir;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    public CommandDispatcher(
        PlaybackEngine playback,
        DeviceManager devices,
        SynthesisEngine synthesis,
        RuntimeState? state = null,
        VoiceRegistry? voiceRegistry = null,
        KokoroInference? inference = null,
        KokoroTokenizer? tokenizer = null,
        string? baseDir = null)
    {
        _playback = playback;
        _devices = devices;
        _synthesis = synthesis;
        _state = state ?? new RuntimeState();
        _voiceRegistry = voiceRegistry;
        _inference = inference;
        _tokenizer = tokenizer;
        _baseDir = baseDir ?? AppContext.BaseDirectory;
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
            "validate_assets" => ValidateAssets(),
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
        var outputDeviceId = GetOptionalString(p, "output_device_id");

        // Resolve opaque device_id to OpenAL device name for per-playback routing
        string? deviceName = null;
        if (outputDeviceId is not null)
        {
            if (!_devices.IsKnownDevice(outputDeviceId))
                throw new RuntimeException("device_unavailable",
                    $"Unknown output device: {outputDeviceId}. Call list_devices first.",
                    retryable: true);
            deviceName = _devices.ResolveDeviceName(outputDeviceId);
        }

        await _playback.PlayAsync(handle, volume, pan, fadeInMs, loop, deviceName);
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
            Features = ["playback", "synthesis", "device_management", "device_routing", "introspection", "asset_validation"],
            Protocol = "ndjson-stdio-v1",
            SynthesisFormat = new AudioFormatInfo
            {
                Container = "wav",
                Encoding = "pcm_s16le",
                SampleRate = 24000,
                Channels = 1,
                BitDepth = 16
            },
            PlaybackFormats = ["wav"]
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

    private ValidateAssetsResult ValidateAssets()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        var modelsDir = Path.Combine(_baseDir, "models");
        var voicesDir = Path.Combine(_baseDir, "voices");
        var espeakDir = Path.Combine(_baseDir, "espeak");

        // ── Model check ──
        var modelPath = Path.Combine(modelsDir, "kokoro.onnx");
        var model = new AssetCheckResult { Path = modelPath };
        if (File.Exists(modelPath))
        {
            model.Available = true;
        }
        else if (!Directory.Exists(modelsDir))
        {
            model.Available = false;
            model.Error = "models/ directory not found";
            model.Hint = $"Create {modelsDir} and place kokoro.onnx inside it";
            errors.Add("Model directory missing");
        }
        else
        {
            model.Available = false;
            model.Error = "kokoro.onnx not found in models/";
            model.Hint = $"Download kokoro.onnx (FP32, ~326 MB) to {modelsDir}";
            errors.Add("Model file missing");
        }

        // ── Voices check ──
        var voiceResult = new VoiceAssetResult { Path = voicesDir };
        if (Directory.Exists(voicesDir))
        {
            var voiceIds = _voiceRegistry?.ListVoices() ?? [];
            voiceResult.Available = voiceIds.Length > 0;
            voiceResult.Count = voiceIds.Length;
            voiceResult.Voices = voiceIds;
            if (voiceIds.Length == 0)
            {
                voiceResult.Error = "No .bin voice files found in voices/";
                voiceResult.Hint = "Place Kokoro voice .bin files (e.g. af_heart.bin) in the voices/ directory";
                warnings.Add("No voice files loaded");
            }
        }
        else
        {
            voiceResult.Available = false;
            voiceResult.Error = "voices/ directory not found";
            voiceResult.Hint = $"Create {voicesDir} and place Kokoro voice .bin files inside it";
            errors.Add("Voices directory missing");
        }

        // ── eSpeak check ──
        var espeak = new AssetCheckResult { Path = espeakDir };
        var espeakAvailable = _tokenizer?.IsEspeakAvailable ?? false;
        espeak.Available = espeakAvailable;
        if (!espeakAvailable)
        {
            espeak.Error = "eSpeak-NG binary not found";
            espeak.Hint = $"Place espeak-ng.exe (or espeak-ng) and espeak-ng-data/ in {espeakDir}, or install espeak-ng on PATH";
            errors.Add("eSpeak-NG not found");
        }
        var espeakData = Path.Combine(espeakDir, "espeak-ng-data");
        if (espeakAvailable && !Directory.Exists(espeakData))
        {
            warnings.Add("espeak-ng-data/ directory not found — eSpeak may use system data");
        }

        // ── ONNX Runtime check ──
        // In NativeAOT single-file, Assembly.Location is empty.
        // If we're running, ONNX Runtime was linked. Check the native DLL instead.
        var onnx = new AssetCheckResult { Available = true, Path = _baseDir };
        var onnxDll = Path.Combine(_baseDir, "onnxruntime.dll");
        var onnxSo = Path.Combine(_baseDir, "libonnxruntime.so");
        if (File.Exists(onnxDll))
            onnx.Path = onnxDll;
        else if (File.Exists(onnxSo))
            onnx.Path = onnxSo;
        // If neither is found as separate file, it's statically linked — still available

        return new ValidateAssetsResult
        {
            Valid = errors.Count == 0,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray(),
            Model = model,
            Voices = voiceResult,
            Espeak = espeak,
            OnnxRuntime = onnx,
            AssetRoot = _baseDir
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

    private static string? GetOptionalString(JsonElement? p, string name)
    {
        if (p is null || !p.Value.TryGetProperty(name, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
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
