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
    public string Version { get; set; } = "0.1.0";

    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = "ndjson-stdio-v1";
}
