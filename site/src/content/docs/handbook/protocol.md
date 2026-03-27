---
title: Protocol Reference
description: ndjson-stdio-v1 wire format, commands, and events.
sidebar:
  order: 3
---

## Wire format

sonic-runtime communicates over **ndjson-stdio-v1** — newline-delimited JSON on stdin (commands) and stdout (responses + events).

### Request

```json
{"id": 1, "method": "version", "params": {}}
```

- `id` — integer, monotonically increasing, echoed in response
- `method` — command name
- `params` — method-specific parameters (optional)

### Response

```json
{"id": 1, "result": {"name": "sonic-runtime", "version": "1.0.1", "protocol": "ndjson-stdio-v1"}}
```

### Error response

```json
{"id": 2, "error": {"code": "invalid_source", "message": "Asset file not found: /missing.wav", "retryable": false}}
```

The `retryable` field tells the caller whether retrying the same request might succeed (e.g., a temporarily unavailable device vs. a permanently invalid parameter).

### Event (unsolicited)

```json
{"event": "playback_ended", "data": {"handle": "h_000000000001", "reason": "completed"}}
```

Events have no `id` — they are pushed by the runtime without a prior request.

## Commands

### version

Returns runtime identity and protocol version. Used as the handshake — sonic-core hard-fails on protocol mismatch.

### load_asset

```json
{"id": 2, "method": "load_asset", "params": {"asset_ref": "file:///path/to/sound.wav"}}
```

Loads a WAV file into an OpenAL buffer. Returns a handle for subsequent commands.

### play

```json
{"id": 3, "method": "play", "params": {"handle": "h_...", "volume": 0.8, "loop": true, "output_device_id": "..."}}
```

Starts playback. `output_device_id` is optional — omit for the default device.

### stop / pause / resume

```json
{"id": 4, "method": "stop", "params": {"handle": "h_..."}}
```

### seek

```json
{"id": 5, "method": "seek", "params": {"handle": "h_...", "position_ms": 5000}}
```

### set_volume / set_pan

```json
{"id": 6, "method": "set_volume", "params": {"handle": "h_...", "level": 0.5, "fade_ms": 200}}
{"id": 7, "method": "set_pan", "params": {"handle": "h_...", "value": -0.3, "ramp_ms": 100}}
```

Volume (`level`): 0.0-1.0. Pan (`value`): -1.0 (left) to 1.0 (right). Fade/ramp durations are optional.

### get_position / get_duration

```json
{"id": 8, "method": "get_position", "params": {"handle": "h_..."}}
{"id": 9, "method": "get_duration", "params": {"handle": "h_..."}}
```

Returns `position_ms` or `duration_ms` respectively. Duration may be null for streams.

### list_devices

Returns all available audio output devices with their IDs, names, and default status. Device IDs are opaque strings (e.g., `openal_0_a1b2c3d4`) used for per-playback routing.

### set_device

```json
{"id": 10, "method": "set_device", "params": {"device_id": "openal_0_a1b2c3d4"}}
```

### synthesize

```json
{"id": 11, "method": "synthesize", "params": {"engine": "kokoro", "voice": "af_heart", "text": "Hello world", "speed": 1.0}}
```

Runs TTS synthesis and returns a playable handle. `engine` must be "kokoro". `speed` range: 0.5-2.0 (default 1.0). The result includes `handle`, `duration_ms`, `sample_rate`, and `channels`.

### Introspection commands

| Method | Description |
|--------|-------------|
| `get_health` | Uptime, active handles, model loaded status, voices count, eSpeak availability |
| `get_capabilities` | Supported engines, features, protocol version, synthesis audio format |
| `list_voices` | All loaded voice IDs with language and gender metadata |
| `preload_model` | Force-load the ONNX model (normally lazy-loaded on first synthesis) |
| `get_model_status` | Whether model is loaded, path, load time, inference count |
| `validate_assets` | Check all synthesis assets (model, voices, eSpeak, ONNX Runtime) with actionable hints |
| `shutdown` | Graceful exit |

## Events

| Event | Data | When |
|-------|------|------|
| `playback_ended` | `handle`, `reason` | Playback completed naturally ("completed") or was stopped ("stopped") |
| `synthesis_started` | `handle`, `engine`, `voice` | TTS pipeline began |
| `synthesis_completed` | `handle`, `duration_ms`, `inference_ms` | TTS inference finished successfully |
| `synthesis_failed` | `handle`, `code`, `message` | TTS inference failed |

## Error codes

| Code | Retryable | Description |
|------|-----------|-------------|
| `invalid_params` | no | Missing or malformed parameters |
| `method_not_found` | no | Unknown method name |
| `playback_not_found` | no | Handle does not exist |
| `device_unavailable` | yes | Requested device not found or unplugged |
| `seek_unsupported` | no | Cannot seek this source type |
| `invalid_source` | no | OpenAL error or asset file not found |
| `unsupported_format` | no | Audio format not supported (non-PCM WAV, bad bit depth) |
| `synthesis_validation_failed` | no | Bad engine, voice, text, or speed value |
| `synthesis_voice_not_found` | no | Requested voice ID not loaded |
| `synthesis_model_missing` | no | ONNX model file not found |
| `synthesis_model_load_failed` | no | ONNX model failed to load |
| `synthesis_inference_failed` | yes | ONNX inference error or empty output |
| `synthesis_not_configured` | no | Synthesis engine not available |
| `internal_error` | no | Unexpected runtime error |

## Diagnostic output

All human-readable diagnostic messages go to **stderr**, prefixed with `[sonic-runtime]`. stdout is reserved exclusively for protocol JSON.
