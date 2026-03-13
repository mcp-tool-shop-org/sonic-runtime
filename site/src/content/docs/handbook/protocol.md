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
{"id": 1, "result": {"name": "sonic-runtime", "version": "0.5.0", "protocol": "ndjson-stdio-v1"}}
```

### Error response

```json
{"id": 2, "error": {"code": "asset_not_found", "message": "File not found: /missing.wav"}}
```

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

### set_volume / set_pan

```json
{"id": 5, "method": "set_volume", "params": {"handle": "h_...", "volume": 0.5}}
{"id": 6, "method": "set_pan", "params": {"handle": "h_...", "pan": -0.3}}
```

Volume: 0.0–1.0. Pan: -1.0 (left) to 1.0 (right).

### list_devices

Returns all available audio output devices with their IDs and default status.

### get_health

Returns runtime health: uptime, active handles, model status, memory.

### get_capabilities

Returns feature flags: supported commands, synthesis availability, device routing support.

## Events

| Event | Data | When |
|-------|------|------|
| `playback_ended` | `handle`, `reason` | Playback completed or was stopped |
| `synthesis_started` | `handle` | TTS inference began |
| `synthesis_completed` | `handle`, `duration_ms` | TTS inference finished |

## Diagnostic output

All human-readable diagnostic messages go to **stderr**, prefixed with `[sonic-runtime]`. stdout is reserved exclusively for protocol JSON.
