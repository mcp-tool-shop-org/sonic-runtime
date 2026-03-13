# sonic-runtime

Native audio runtime sidecar for [sonic-core](https://github.com/mcp-tool-shop-org/sonic-core).

## What This Is

A subprocess sidecar that handles the native audio concerns sonic-core delegates:

- **Playback** — load, play, pause, resume, stop, seek, fade, volume, pan, loop
- **Device control** — enumerate outputs, switch devices, handle hot-plug
- **Synthesis** — Kokoro/ONNX TTS inference producing playable audio

sonic-core launches this as a child process and communicates over newline-delimited JSON on stdio.

## What This Is Not

- Not a standalone application
- Not a UI
- Not a session manager, preset store, or product layer
- No leases (owned by sonic-core)
- No user state, therapy concepts, or product semantics

See [ADR-0005](https://github.com/mcp-tool-shop-org/sonic-core/blob/main/docs/adr/0005-native-runtime-boundary.md) and [ADR-0006](https://github.com/mcp-tool-shop-org/sonic-core/blob/main/docs/adr/0006-runtime-language-stack.md) for the architectural rationale.

## Stack

- **C# / .NET 8 LTS** with **NativeAOT** — single native binary, no JIT, no runtime dependency
- **SoundFlow v1.1.1** — MiniAudio-backed playback engine (won eval over NAudio; see `docs/backend-evaluation.md`)
- **ONNX Runtime 1.22.0** — Kokoro TTS inference via managed C# binding (NativeAOT-compatible)
- **eSpeak-NG 1.52.0** — grapheme-to-phoneme conversion (spawned as child process)
- **Windows-first** (v1)

## Build

```bash
dotnet build
```

## Test

```bash
dotnet test
```

## Publish (NativeAOT)

```bash
dotnet publish src/SonicRuntime -c Release -r win-x64
```

Output: `src/SonicRuntime/bin/Release/net8.0/win-x64/publish/SonicRuntime.exe`

## Protocol

See [docs/protocol.md](docs/protocol.md) for the full wire protocol specification.

Quick example:

```
→ {"id":1,"method":"version"}
← {"id":1,"result":{"name":"sonic-runtime","version":"0.3.0","protocol":"ndjson-stdio-v1"}}

→ {"id":2,"method":"load_asset","params":{"asset_ref":"file:///rain.wav"}}
← {"id":2,"result":{"handle":"h_000000000001"}}

→ {"id":3,"method":"play","params":{"handle":"h_000000000001","volume":0.8,"loop":true}}
← {"id":3,"result":null}

→ {"id":4,"method":"get_health"}
← {"id":4,"result":{"status":"ok","uptime_ms":12345,"active_handles":1,"model_loaded":true,...}}
```

## Architecture

```
stdin (JSON) → CommandLoop → CommandDispatcher → Engine components → stdout (JSON)
                                                  ├─ PlaybackEngine (SoundFlow)
                                                  ├─ DeviceManager (hot-plug, enumeration)
                                                  ├─ SynthesisEngine (Kokoro ONNX → WAV → playback)
                                                  │   ├─ KokoroTokenizer (eSpeak G2P)
                                                  │   ├─ KokoroInference (ONNX Runtime)
                                                  │   └─ VoiceRegistry (510 voices, raw float32)
                                                  └─ RuntimeState (handle tracking)
```

All diagnostic output goes to **stderr**. stdout is exclusively for protocol messages and runtime events.

## Synthesis Assets

Real synthesis requires model files, voice embeddings, and eSpeak-NG. See [docs/synthesis-assets.md](docs/synthesis-assets.md) for the full operator contract.

## Status

**v0.3.0** — Working native synthesis/playback sidecar with runtime introspection.

What's implemented:
- Real SoundFlow playback (load, play, pause, resume, stop, seek, fade, volume, pan, loop)
- Device enumeration and hot-plug handling
- Kokoro ONNX synthesis (text → phonemes → inference → WAV → playback, ~5× realtime on CPU)
- Runtime introspection (get_health, get_capabilities, list_voices, preload_model, get_model_status)
- Thread-safe event infrastructure for unsolicited runtime messages
- 73 tests (unit + real-asset integration)

Next:
1. Wire engine event emission (playback_ended, synthesis_started/completed) via IEventWriter
2. Add SidecarBackend to sonic-core
3. Integration tests over real subprocess

## License

MIT
