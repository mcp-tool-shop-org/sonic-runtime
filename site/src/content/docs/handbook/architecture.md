---
title: Architecture
description: Engine components, OpenAL backend, and the command loop.
sidebar:
  order: 2
---

## Overview

```
stdin (JSON) → CommandLoop → CommandDispatcher → Engine components → stdout (JSON)
                    │                             ├─ PlaybackEngine (OpenAL Soft)
                    │                             ├─ DeviceManager (hot-plug, enumeration)
                    │                             ├─ SynthesisEngine (Kokoro ONNX → WAV → playback)
                    │                             │   ├─ KokoroTokenizer (eSpeak G2P)
                    │                             │   ├─ KokoroInference (ONNX Runtime)
                    │                             │   └─ VoiceRegistry (510 voices)
                    │                             └─ RuntimeState (handle tracking)
                    └─ IEventWriter → stdout (unsolicited events)
```

All diagnostic output goes to **stderr**. stdout is exclusively for protocol messages and events.

## CommandLoop

The main entry point. Reads newline-delimited JSON from stdin, dispatches to CommandDispatcher, writes responses to stdout. Single-threaded command processing ensures deterministic ordering.

## PlaybackEngine

Manages audio playback through OpenAL Soft (via Silk.NET bindings):

- **Load** — parse WAV files into OpenAL buffers
- **Play** — create OpenAL sources, attach buffers, start playback
- **Controls** — volume, pan, seek, fade, loop
- **Completion** — 10ms polling of `AL_SOURCE_STATE` detects when playback ends
- **Device routing** — each playback can target a specific output device via separate OpenAL device/context pairs

## DeviceManager

Enumerates audio output devices via `ALC_ENUMERATE_ALL_EXT`:

- Lists real hardware endpoints (not just "default")
- Provides device ID ↔ OpenAL device name mapping
- Handles hot-plug events when devices connect/disconnect

## SynthesisEngine

Text-to-speech pipeline:

1. **KokoroTokenizer** — spawns eSpeak-NG to convert text to phoneme sequences
2. **KokoroInference** — runs the Kokoro ONNX model to generate audio samples
3. **WavWriter** — packages float32 samples into a WAV buffer
4. **PlaybackEngine** — plays the generated WAV through OpenAL

Performance: approximately 5x realtime on CPU.

## RuntimeState

Tracks active handles, maps them to playback slots, and manages lifecycle. Handle format: `h_XXXXXXXXXXXX` (12-digit hex).

## Event system

Events flow from engine components to the parent process via `IEventWriter`:

- `playback_ended` — a playback completed or was stopped
- `synthesis_started` / `synthesis_completed` — TTS pipeline lifecycle

Events are unsolicited JSON messages on stdout, distinct from request/response pairs (they have no `id` field).
