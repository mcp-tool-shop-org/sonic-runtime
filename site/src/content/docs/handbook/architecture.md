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
                    │                             │   └─ VoiceRegistry (style embeddings)
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

Text-to-speech pipeline using Kokoro ONNX:

1. **KokoroTokenizer** — normalizes text (currency, titles, decimals), spawns eSpeak-NG for grapheme-to-phoneme conversion, maps IPA output to 178-token vocab, pads for model input
2. **VoiceRegistry** — loads raw float32 `.bin` voice embeddings at startup (shape: 510 entries x 256 floats per voice). Selects a 256-float style vector based on token count
3. **KokoroInference** — runs the Kokoro ONNX model (lazy-loaded, held for process lifetime). Inputs: token IDs, style vector, speed. Output: float32 PCM at 24 kHz. Thread-safe via lock
4. **WavWriter** — converts float32 samples to 16-bit PCM WAV
5. **PlaybackEngine** — loads the generated WAV into an OpenAL buffer for playback

Performance: approximately 5x realtime on CPU. Speed parameter accepted in range 0.5-2.0.

## RuntimeState

Tracks active handles, maps them to playback slots, and manages lifecycle. Handle format: `h_XXXXXXXXXXXX` (12-digit hex).

## Event system

Events flow from engine components to the parent process via `IEventWriter`. The `CommandLoopEventWriter` bridges engine events to the CommandLoop's stdout writer using late binding (via `Connect()`) to break the circular dependency between engines and the loop.

Event types:

- `playback_ended` — a playback completed naturally or was stopped. Data: `handle`, `reason` ("completed" or "stopped")
- `synthesis_started` — TTS inference began. Data: `handle`, `engine`, `voice`
- `synthesis_completed` — TTS inference finished. Data: `handle`, `duration_ms`, `inference_ms`
- `synthesis_failed` — TTS inference failed. Data: `handle`, `code`, `message`

Events are unsolicited JSON messages on stdout, distinct from request/response pairs (they have no `id` field). A `NullEventWriter` is available for test isolation.
