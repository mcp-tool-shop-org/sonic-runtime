using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonicRuntime.Protocol;

/// <summary>
/// Inbound request from sonic-core over stdin.
/// </summary>
public sealed class RuntimeRequest
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("method")]
    public string Method { get; set; } = "";

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }
}

/// <summary>
/// Outbound success response to sonic-core over stdout.
/// </summary>
public sealed class RuntimeResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("result")]
    public object? Result { get; set; }
}

/// <summary>
/// Outbound error response to sonic-core over stdout.
/// </summary>
public sealed class RuntimeErrorResponse
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("error")]
    public RuntimeError Error { get; set; } = default!;
}

/// <summary>
/// Error shape matching SonicError in @sonic-core/types.
/// </summary>
public sealed class RuntimeError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("retryable")]
    public bool Retryable { get; set; }
}

/// <summary>
/// Source-generated JSON context for NativeAOT-safe serialization.
/// No reflection. No runtime code generation.
/// </summary>
[JsonSerializable(typeof(RuntimeRequest))]
[JsonSerializable(typeof(RuntimeResponse))]
[JsonSerializable(typeof(RuntimeErrorResponse))]
[JsonSerializable(typeof(RuntimeError))]
[JsonSerializable(typeof(HandleResult))]
[JsonSerializable(typeof(PositionResult))]
[JsonSerializable(typeof(DurationResult))]
[JsonSerializable(typeof(DeviceInfo))]
[JsonSerializable(typeof(DeviceInfo[]))]
[JsonSerializable(typeof(VersionResult))]
[JsonSerializable(typeof(HealthResult))]
[JsonSerializable(typeof(CapabilitiesResult))]
[JsonSerializable(typeof(VoiceInfo))]
[JsonSerializable(typeof(VoiceInfo[]))]
[JsonSerializable(typeof(ModelStatusResult))]
[JsonSerializable(typeof(PreloadResult))]
[JsonSerializable(typeof(AssetCheckResult))]
[JsonSerializable(typeof(VoiceAssetResult))]
[JsonSerializable(typeof(ValidateAssetsResult))]
[JsonSerializable(typeof(RuntimeEvent))]
[JsonSerializable(typeof(PlaybackEndedData))]
[JsonSerializable(typeof(SynthesisStartedData))]
[JsonSerializable(typeof(SynthesisTimingData))]
[JsonSerializable(typeof(SynthesisFailedData))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class RuntimeJsonContext : JsonSerializerContext;

// ── Result types ──

public sealed class HandleResult
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; set; }

    [JsonPropertyName("sample_rate")]
    public int? SampleRate { get; set; }

    [JsonPropertyName("channels")]
    public int? Channels { get; set; }
}

public sealed class PositionResult
{
    [JsonPropertyName("position_ms")]
    public long PositionMs { get; set; }
}

public sealed class DurationResult
{
    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; set; }
}

public sealed class DeviceInfo
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "output";

    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("channels")]
    public int Channels { get; set; }

    [JsonPropertyName("sample_rates")]
    public int[] SampleRates { get; set; } = [];
}

public sealed class VersionResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "sonic-runtime";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.4.1";

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "ndjson-stdio-v1";
}

// ── Introspection result types (v0.3.0) ──

public sealed class HealthResult
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "ok";

    [JsonPropertyName("uptime_ms")]
    public long UptimeMs { get; set; }

    [JsonPropertyName("active_handles")]
    public int ActiveHandles { get; set; }

    [JsonPropertyName("model_loaded")]
    public bool ModelLoaded { get; set; }

    [JsonPropertyName("voices_loaded")]
    public int VoicesLoaded { get; set; }

    [JsonPropertyName("espeak_available")]
    public bool EspeakAvailable { get; set; }
}

public sealed class CapabilitiesResult
{
    [JsonPropertyName("engines")]
    public string[] Engines { get; set; } = ["kokoro"];

    [JsonPropertyName("features")]
    public string[] Features { get; set; } = [];

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "ndjson-stdio-v1";
}

public sealed class VoiceInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "";
}

public sealed class ModelStatusResult
{
    [JsonPropertyName("loaded")]
    public bool Loaded { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("load_time_ms")]
    public long LoadTimeMs { get; set; }

    [JsonPropertyName("inference_count")]
    public int InferenceCount { get; set; }
}

public sealed class PreloadResult
{
    [JsonPropertyName("loaded")]
    public bool Loaded { get; set; }

    [JsonPropertyName("load_time_ms")]
    public long LoadTimeMs { get; set; }
}

// ── Asset validation result types (v0.4.0) ──

public sealed class AssetCheckResult
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }
}

public sealed class VoiceAssetResult
{
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("voices")]
    public string[]? Voices { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("hint")]
    public string? Hint { get; set; }
}

public sealed class ValidateAssetsResult
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    [JsonPropertyName("errors")]
    public string[] Errors { get; set; } = [];

    [JsonPropertyName("warnings")]
    public string[] Warnings { get; set; } = [];

    [JsonPropertyName("model")]
    public AssetCheckResult Model { get; set; } = new();

    [JsonPropertyName("voices")]
    public VoiceAssetResult Voices { get; set; } = new();

    [JsonPropertyName("espeak")]
    public AssetCheckResult Espeak { get; set; } = new();

    [JsonPropertyName("onnx_runtime")]
    public AssetCheckResult OnnxRuntime { get; set; } = new();

    [JsonPropertyName("asset_root")]
    public string? AssetRoot { get; set; }
}

// ── Event types (v0.3.0) ──

/// <summary>
/// Unsolicited event message (no id field).
/// Sent interleaved with responses on stdout.
/// </summary>
public sealed class RuntimeEvent
{
    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public sealed class PlaybackEndedData
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "completed";
}

public sealed class SynthesisStartedData
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("engine")]
    public string Engine { get; set; } = "";

    [JsonPropertyName("voice")]
    public string Voice { get; set; } = "";
}

public sealed class SynthesisTimingData
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; set; }

    [JsonPropertyName("inference_ms")]
    public long? InferenceMs { get; set; }
}

public sealed class SynthesisFailedData
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
