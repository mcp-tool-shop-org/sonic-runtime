---
title: Beginners Guide
description: A step-by-step introduction to sonic-runtime for newcomers.
sidebar:
  order: 99
---

## What is sonic-runtime?

sonic-runtime is a native audio engine that runs as a sidecar process. It handles audio playback, device management, and text-to-speech synthesis on behalf of [sonic-core](https://github.com/mcp-tool-shop-org/sonic-core), which communicates with it over newline-delimited JSON on stdin/stdout.

sonic-runtime is not a standalone application. It expects a parent process (sonic-core) to launch it, send commands, and receive responses and events.

## Prerequisites

Before you begin, make sure you have:

- **.NET 8 SDK** installed ([download](https://dotnet.microsoft.com/en-us/download/dotnet/8.0))
- **Windows** (the v1 binary targets win-x64)
- **Git** for cloning the repository

For synthesis (text-to-speech), you also need:
- The Kokoro ONNX model file (~326 MB)
- Voice embedding files (`.bin` format)
- eSpeak-NG installed or available in the `espeak/` directory

Playback works without any synthesis assets.

## Installation

Clone the repository and build:

```bash
git clone https://github.com/mcp-tool-shop-org/sonic-runtime
cd sonic-runtime
dotnet build
```

To create a self-contained native executable (no .NET runtime needed on the target machine):

```bash
dotnet publish src/SonicRuntime -c Release -r win-x64
```

The output binary is at `src/SonicRuntime/bin/Release/net8.0/win-x64/publish/SonicRuntime.exe`.

## Core concepts

### Handles

Every piece of audio in sonic-runtime is tracked by an opaque **handle** (e.g., `h_000000000001`). You get a handle when you load an asset or synthesize speech, and use that handle for all subsequent operations (play, pause, stop, seek, volume, pan).

Handles are internal to sonic-runtime. The parent process (sonic-core) maps them to its own playback IDs -- clients never see raw handles.

### The protocol

sonic-runtime communicates using **ndjson-stdio-v1** -- one JSON object per line on stdin (commands) and stdout (responses and events).

A request looks like:
```json
{"id": 1, "method": "version"}
```

A response echoes the `id`:
```json
{"id": 1, "result": {"name": "sonic-runtime", "version": "1.0.1", "protocol": "ndjson-stdio-v1"}}
```

Errors include a structured error object with a `code`, `message`, and `retryable` flag:
```json
{"id": 2, "error": {"code": "invalid_source", "message": "Asset file not found", "retryable": false}}
```

Events are pushed by the runtime without a prior request and have no `id`:
```json
{"event": "playback_ended", "data": {"handle": "h_000000000001", "reason": "completed"}}
```

All diagnostic logs go to **stderr**. stdout is exclusively for protocol messages.

### Engines and components

sonic-runtime has three main engine components:

1. **PlaybackEngine** -- loads WAV files into OpenAL buffers, manages sources, handles play/pause/stop/seek/volume/pan/loop. Detects natural completion via 10ms polling.
2. **DeviceManager** -- enumerates real hardware audio output devices. Each playback can target a specific device.
3. **SynthesisEngine** -- converts text to speech using Kokoro ONNX. Pipeline: text normalization, eSpeak G2P, ONNX inference, WAV generation.

## Usage example

Here is a typical command sequence. Each line is one JSON object sent to stdin:

```
→ {"id":1,"method":"version"}
← {"id":1,"result":{"name":"sonic-runtime","version":"1.0.1","protocol":"ndjson-stdio-v1"}}

→ {"id":2,"method":"load_asset","params":{"asset_ref":"file:///C:/sounds/rain.wav"}}
← {"id":2,"result":{"handle":"h_000000000001"}}

→ {"id":3,"method":"play","params":{"handle":"h_000000000001","volume":0.8,"loop":true}}
← {"id":3,"result":null}

→ {"id":4,"method":"set_volume","params":{"handle":"h_000000000001","level":0.5,"fade_ms":500}}
← {"id":4,"result":null}

→ {"id":5,"method":"stop","params":{"handle":"h_000000000001"}}
← {"id":5,"result":null}
← {"event":"playback_ended","data":{"handle":"h_000000000001","reason":"stopped"}}
```

For synthesis:
```
→ {"id":6,"method":"synthesize","params":{"engine":"kokoro","voice":"af_heart","text":"Hello world","speed":1.0}}
← {"event":"synthesis_started","data":{"handle":"h_000000000002","engine":"kokoro","voice":"af_heart"}}
← {"id":6,"result":{"handle":"h_000000000002","duration_ms":850,"sample_rate":24000,"channels":1}}
← {"event":"synthesis_completed","data":{"handle":"h_000000000002","duration_ms":850,"inference_ms":270}}

→ {"id":7,"method":"play","params":{"handle":"h_000000000002"}}
← {"id":7,"result":null}
```

## Validating your setup

Before running synthesis, you can check that all required assets are in place using the `validate_assets` command:

```
→ {"id":1,"method":"validate_assets"}
← {"id":1,"result":{"valid":true,"errors":[],"warnings":[],"model":{"available":true,"path":"..."},"voices":{"available":true,"count":10,"voices":["af_heart","am_onyx",...]},"espeak":{"available":true,"path":"..."},"onnx_runtime":{"available":true,"path":"..."},"asset_root":"..."}}
```

If any asset is missing, the response includes an `errors` array and each asset check includes an `error` message and a `hint` telling you exactly what to do. For example, a missing model returns:

```json
{"error": "kokoro.onnx not found in models/", "hint": "Download kokoro.onnx (FP32, ~326 MB) to C:\\publish\\models"}
```

You can also check the runtime health at any time:

```
→ {"id":2,"method":"get_health"}
← {"id":2,"result":{"status":"ok","uptime_ms":12345,"active_handles":0,"model_loaded":true,"voices_loaded":10,"espeak_available":true}}
```

## Device routing

sonic-runtime supports per-playback device routing. You can list available audio output devices and direct any playback to a specific one:

```
→ {"id":10,"method":"list_devices"}
← {"id":10,"result":[{"device_id":"openal_0_a1b2c3d4","name":"Speakers (Realtek)","kind":"output","is_default":true,"channels":2,"sample_rates":[44100,48000]},{"device_id":"openal_1_e5f6a7b8","name":"Headphones (USB)","kind":"output","is_default":false,"channels":2,"sample_rates":[44100,48000]}]}

→ {"id":11,"method":"play","params":{"handle":"h_000000000001","volume":0.8,"output_device_id":"openal_1_e5f6a7b8"}}
← {"id":11,"result":null}
```

Device IDs are opaque strings that change when hardware is reconnected. Always call `list_devices` before routing to a specific device.

## Running the tests

```bash
dotnet test
```

The test suite covers all protocol methods, engine components, event emission, error handling, and version alignment. Tests that require real audio hardware or synthesis assets are isolated and use mock backends.

## Common errors

| Error code | What it means | What to do |
|------------|---------------|------------|
| `invalid_source` | The WAV file path does not exist or is not a valid WAV | Check the `asset_ref` path. Only WAV files are supported. |
| `playback_not_found` | The handle has already been stopped or never existed | Do not reuse handles after `stop`. Load a new asset. |
| `device_unavailable` | The requested output device is not connected | Call `list_devices` first. Device IDs change when hardware is reconnected. |
| `synthesis_model_missing` | The `models/kokoro.onnx` file is not present | Download the model from HuggingFace and place it in `models/` next to the binary. |
| `synthesis_voice_not_found` | The requested voice ID is not loaded | Check available voices with `list_voices`. Voice files must be `.bin` files in `voices/`. |
| `synthesis_validation_failed` | Bad input: wrong engine name, empty text, or speed out of range | Engine must be "kokoro". Text must not be empty. Speed must be 0.5-2.0. |

## Next steps

- Read the [Architecture](/sonic-runtime/handbook/architecture/) page to understand how the components fit together
- Read the [Protocol Reference](/sonic-runtime/handbook/protocol/) for the complete list of commands and events
- Read the [Security](/sonic-runtime/handbook/security/) page for the threat model
